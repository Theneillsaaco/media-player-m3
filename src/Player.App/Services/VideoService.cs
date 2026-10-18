using System.Globalization;
using Player.Core;
using Player.Core.Contracts;
using Player.Core.Domain;
using Player.Core.Playback;

namespace Player.App.Services;

/// <summary>What is known about the video of the current track. Version goes up with every change.</summary>
internal sealed record VideoSnapshot(string TrackId, bool Checked, VideoPlan Plan, VideoStream? Stream, string Error, int Version);

/// <summary>
/// The video side of the current track: first what it is (official video, studio audio, upload, and its
/// counterpart), which is cheap and asked for every track; then, only when the video is going to be on screen,
/// its stream. Both run on the thread pool and are kept in memory (streams until shortly before they expire);
/// the UI reads <see cref="Snapshot"/> from its poll. A failed request is left alone for thirty seconds unless
/// the user asks again with <see cref="Retry"/>.
/// </summary>
internal sealed class VideoService(
    IVideoSource? source,
    Func<string, string?> readSetting,
    Action<string, string> writeSetting,
    Func<long>? clock = null)
{
    private const long RetryAfterSeconds = 30;
    private const int MaxStreams = 6;
    private const string AutoKey = "video.auto";

    private readonly Lock _gate = new();
    private readonly Dictionary<string, VideoInfo> _info = [];
    private readonly Dictionary<string, VideoStream> _streams = [];
    private readonly List<string> _streamOrder = []; // oldest first
    private readonly HashSet<string> _inflight = [];
    private readonly Dictionary<string, long> _failedAt = [];
    private readonly Dictionary<string, string> _errors = [];
    private string _trackId = "";
    private int _version;

    /// <summary>Show official music videos instead of the cover without being asked. On by default.</summary>
    public bool Auto
    {
        get
        {
            try { return readSetting(AutoKey) != "0"; }
            catch (Exception ex)
            {
                Log.Warn("video", "read setting", ex);
                return true;
            }
        }
    }

    public void SaveAuto(bool on)
    {
        try { writeSetting(AutoKey, on ? "1" : "0"); }
        catch (Exception ex) { Log.Warn("video", "save setting", ex); }
    }

    public VideoSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                var plan = _info.TryGetValue(_trackId, out var info) ? Video.plan(info) : Video.none;
                VideoStream? stream = plan.Available && _streams.TryGetValue(plan.VideoId, out var found) && Video.isFresh(Now(), found) ? found : null;
                var error = _errors.GetValueOrDefault("i:" + _trackId) ?? (plan.Available ? _errors.GetValueOrDefault("s:" + plan.VideoId) : null) ?? "";
                return new VideoSnapshot(_trackId, _info.ContainsKey(_trackId), plan, stream, error, _version);
            }
        }
    }

    /// <summary>Makes this the current track and looks up what it is. Asking again for the same track does nothing.</summary>
    public void Select(Track? track)
    {
        var id = track is null || track.Id.Length == 0 || TrackSource.IsDirect(track.Id) ? "" : track.Id;

        lock (_gate)
        {
            if (id == _trackId)
                return;

            _trackId = id;
            _version++;
        }

        EnsureInfo(id);
    }

    /// <summary>Starts fetching the stream of the current track's video, if there is one and it is not already here.</summary>
    public void EnsureStream()
    {
        string videoId;

        lock (_gate)
        {
            if (!_info.TryGetValue(_trackId, out var info) || Video.plan(info) is not { Available: true } plan)
                return;

            videoId = plan.VideoId;
            if (_streams.TryGetValue(videoId, out var cached) && Video.isFresh(Now(), cached))
                return;
            if (!StartAllowed("s:" + videoId))
                return;
        }

        _ = Task.Run(() => FetchStreamAsync(videoId));
    }

    /// <summary>The player could not play this stream (expired, refused): drop it and leave it alone for a while.</summary>
    public void ReportFailure(string videoId, string message)
    {
        lock (_gate)
        {
            _streams.Remove(videoId);
            _streamOrder.Remove(videoId);
            _failedAt["s:" + videoId] = Now();
            _errors["s:" + videoId] = message;
            _version++;
        }
    }

    /// <summary>Forgets earlier failures of the current track and asks again.</summary>
    public void Retry()
    {
        string id;

        lock (_gate)
        {
            id = _trackId;
            _failedAt.Clear();
            _errors.Clear();
            _version++;
        }

        EnsureInfo(id);
        EnsureStream();
    }

    private void EnsureInfo(string id)
    {
        if (id.Length == 0 || source is null)
            return;

        lock (_gate)
        {
            if (_info.ContainsKey(id) || !StartAllowed("i:" + id))
                return;
        }

        _ = Task.Run(() => FetchInfoAsync(id));
    }

    /// <summary>True (and marks the request as running) when nothing is running for it and it did not just fail. Call under the lock.</summary>
    private bool StartAllowed(string key)
    {
        if (source is null || _inflight.Contains(key))
            return false;
        if (_failedAt.TryGetValue(key, out var at) && Now() - at < RetryAfterSeconds)
            return false;

        _inflight.Add(key);
        return true;
    }

    private async Task FetchInfoAsync(string id)
    {
        try
        {
            var info = await source!.GetVideoInfo(id);

            lock (_gate)
            {
                _info[id] = info;
                Finish("i:" + id, null);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("video", "info for " + id, ex);
            lock (_gate) Finish("i:" + id, Tr.T("video.checkFailed"));
        }
    }

    private async Task FetchStreamAsync(string videoId)
    {
        try
        {
            var stream = await source!.GetVideoStream(videoId);

            lock (_gate)
            {
                _streams[videoId] = stream;
                _streamOrder.Remove(videoId);
                _streamOrder.Add(videoId);

                while (_streamOrder.Count > MaxStreams)
                {
                    _streams.Remove(_streamOrder[0]);
                    _streamOrder.RemoveAt(0);
                }

                Finish("s:" + videoId, null);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("video", "stream for " + videoId, ex);
            lock (_gate) Finish("s:" + videoId, Tr.T("video.loadFailed"));
        }
    }

    /// <summary>Marks a request as done: clears its failure on success, records it otherwise. Call under the lock.</summary>
    private void Finish(string key, string? error)
    {
        _inflight.Remove(key);

        if (error is null)
        {
            _failedAt.Remove(key);
            _errors.Remove(key);
        }
        else
        {
            _failedAt[key] = Now();
            _errors[key] = error;
        }

        _version++;
    }

    private long Now() => clock?.Invoke() ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
