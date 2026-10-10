using System.Collections.Concurrent;
using Mpris.DBus;
using Tmds.DBus.Protocol;

namespace Player.Mpris;

/// <summary>
/// org.mpris.MediaPlayer2 on the session bus, so playerctl, media keys and desktop widgets can see and
/// control the player. D-Bus callbacks arrive on pool threads: they never touch the app, they only leave
/// a <see cref="MprisCommand"/> in a queue that the UI drains with <see cref="TryDequeue"/>. The UI pushes
/// state with <see cref="Publish"/>.
/// </summary>
public class MprisService : IDisposable
{
    /// <summary>Connects and claims org.mpris.MediaPlayer2.player (or a per-process name if that one is taken).</summary>
    public async Task StartAsync()
    {
        var address = DBusAddress.Session ?? throw new InvalidOperationException("no session bus");
        var connection = new DBusConnection(address);
        await connection.ConnectAsync();
        var handler = new Handler(this, connection);
        connection.AddMethodHandler(handler);
        
        const string Name = "org.mpris.MediaPlayer2.Player";
        if (!await connection.TryRequestNameAsync(Name, RequestNameOptions.None))
            await connection.RequestNameAsync($"{Name}.instance{Environment.ProcessId}");
        
        _connection = connection;
        _handler = handler;
    }
    
    public bool TryDequeue(out MprisCommand command) => _commands.TryDequeue(out command);
    
    /// <summary>Sets the state clients see; emits PropertiesChanged for what differs from the previous one.</summary>
    public void Publish(MprisState state)
    {
        var previous = Interlocked.Exchange(ref _state, state);
        
        if (_connection is not { } connection || _handler is not { } handler)
            return;

        List<PlayerProperty> changed = [];
        
        if (previous.Status != state.Status)
            changed.Add(PlayerProperty.PlaybackStatus);
        if (previous.Loop != state.Loop)
            changed.Add(PlayerProperty.LoopStatus);
        if (previous.Shuffle != state.Shuffle)
            changed.Add(PlayerProperty.Shuffle);
        if (Math.Abs(previous.Volume - state.Volume) > 0.001)
            changed.Add(PlayerProperty.Volume);
        if (previous.CanGoNext != state.CanGoNext)
            changed.Add(PlayerProperty.CanGoNext);
        if (previous.CanGoPrevious != state.CanGoPrevious)
            changed.Add(PlayerProperty.CanGoPrevious);
        if (previous.CanPlay != state.CanPlay)
            changed.Add(PlayerProperty.CanPlay);
        if (previous.CanSeek != state.CanSeek)
            changed.Add(PlayerProperty.CanSeek);
        if (!SameMetadata(previous, state))
            changed.Add(PlayerProperty.Metadata);
        if (changed.Count == 0)
            return;

        try
        {
            connection.EmitPropertiesChanged(Root, handler, changed.ToArray());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[mpris] " + ex.Message);
        }
    }
    
    /// <summary>Tells clients the position jumped (a seek, or a restart of the track).</summary>
    public void Seeked(long positionMicros)
    {
        if (_connection is not { } connection || _handler is null)
            return;

        try
        {
            connection.EmitSeeked(Root, positionMicros);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[mpris] " + ex.Message);
        }
    }
    
    public void Dispose()
    {
        _handler = null;
        _connection?.Dispose();
        _connection = null;
    }
    
    private static bool SameMetadata(MprisState a, MprisState b) =>
        a.TrackKey == b.TrackKey && a.Title == b.Title && a.Album == b.Album && a.ArtUrl == b.ArtUrl
        && a.LengthMicros == b.LengthMicros && a.Artists.SequenceEqual(b.Artists);
    
    /// <summary>"/org/mpris/MediaPlayer2/Track/&lt;key&gt;" with only the characters an object path allows.</summary>
    private static string TrackPath(string key)
    {
        if (key.Length == 0) return NoTrack;
        var safe = new string([.. key.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_')]);
        return "/org/mpris/MediaPlayer2/Track/" + (safe.Length > 120 ? safe[..120] : safe);
    }
    
    private sealed class Handler(MprisService owner, DBusConnection connection) : DBusHandler(connection, Root, handlesChildPaths: false, handleOnCapturedContext: false), IMediaPlayer2Handler, IMediaPlayer2Properties, IPlayerHandler, IPlayerProperties
    {
        private MprisState State => owner._state;
        
        private void Send(MprisAction action, double number = 0, string text = "") =>
            owner._commands.Enqueue(new MprisCommand(action, number, text));
        
        // ---- org.mpris.MediaPlayer2 ----
        public bool CanQuit => false;
        public bool CanRaise => false;
        public bool CanSetFullscreen => false;
        public bool Fullscreen { get => false; set { } }
        public bool HasTrackList => false;
        public string Identity => owner._identity;
        public string DesktopEntry => "";
        public string[] SupportedUriSchemes => ["http", "https"];
        public string[] SupportedMimeTypes => [];

        ValueTask IMediaPlayer2Handler.RaiseAsync() => default;
        ValueTask IMediaPlayer2Handler.QuitAsync() => default;
        
        // ---- org.mpris.MediaPlayer2.Player ----
        public string PlaybackStatus => State.Status;
        public string LoopStatus { get => State.Loop; set => Send(MprisAction.SetLoop, text: value); }
        public double Rate { get => 1.0; set { } }
        public bool Shuffle { get => State.Shuffle; set => Send(MprisAction.SetShuffle, value ? 1 : 0); }
        public double Volume { get => State.Volume; set => Send(MprisAction.SetVolume, value); }
        public long Position => State.PositionMicros;
        public double MinimumRate => 1.0;
        public double MaximumRate => 1.0;
        public bool CanGoNext => State.CanGoNext;
        public bool CanGoPrevious => State.CanGoPrevious;
        public bool CanPlay => State.CanPlay;
        public bool CanPause => !State.CanPlay;
        public bool CanSeek => State.CanSeek;
        public bool CanControl => true;

        public Dictionary<string, VariantValue> Metadata
        {
            get
            {
                var s = State;
                var metadata = new Dictionary<string, VariantValue>
                {
                    ["mpris:trackid"] = new ObjectPath(TrackPath(s.TrackKey))
                };
                if (s.TrackKey.Length == 0)
                    return metadata;
                
                if (s.LengthMicros > 0)
                    metadata["mpris:length"] = VariantValue.Int64(s.LengthMicros);
                if (s.ArtUrl.Length > 0)
                    metadata["mpris:artUrl"] = s.ArtUrl;
                if (s.Title.Length > 0)
                    metadata["xesam:title"] = s.Title;
                if (s.Album.Length > 0)
                    metadata["xesam:album"] = s.Album;
                if (s.Artists.Count > 0)
                    metadata["xesam:artist"] = VariantValue.Array(s.Artists.ToArray());
                
                return metadata;
            }
        }
        
        ValueTask IPlayerHandler.NextAsync() { Send(MprisAction.Next); return default; }
        ValueTask IPlayerHandler.PreviousAsync() { Send(MprisAction.Previous); return default; }
        ValueTask IPlayerHandler.PauseAsync() { Send(MprisAction.Pause); return default; }
        ValueTask IPlayerHandler.PlayPauseAsync() { Send(MprisAction.PlayPause); return default; }
        ValueTask IPlayerHandler.StopAsync() { Send(MprisAction.Stop); return default; }
        ValueTask IPlayerHandler.PlayAsync() { Send(MprisAction.Play); return default; }
        ValueTask IPlayerHandler.SeekAsync(long offset) { Send(MprisAction.SeekRelative, offset); return default; }

        ValueTask IPlayerHandler.SetPositionAsync(ObjectPath trackId, long position)
        {
            // The spec says to ignore it when it names a track that is no longer the current one.
            if (trackId.ToString() == TrackPath(State.TrackKey))
                Send(MprisAction.SeekTo, position);
            
            return default;
        }
        
        ValueTask IPlayerHandler.OpenUriAsync(string uri) => default;

        // ---- property plumbing, generated ----
        ValueTask IMediaPlayer2Handler.HandleGetPropertyAsync(IMediaPlayer2Handler.GetPropertyContext context) => context.Handle(this);
        ValueTask IMediaPlayer2Handler.HandleGetAllPropertiesAsync(IMediaPlayer2Handler.GetAllPropertiesContext context) => context.Handle(this);
        ValueTask IMediaPlayer2Handler.HandleSetPropertyAsync(IMediaPlayer2Handler.SetPropertyContext context) => context.Handle(this);
        ValueTask IPlayerHandler.HandleGetPropertyAsync(IPlayerHandler.GetPropertyContext context) => context.Handle(this);
        ValueTask IPlayerHandler.HandleGetAllPropertiesAsync(IPlayerHandler.GetAllPropertiesContext context) => context.Handle(this);
        ValueTask IPlayerHandler.HandleSetPropertyAsync(IPlayerHandler.SetPropertyContext context) => context.Handle(this);
    }
    
    #region Fields

    private const string Root = "/org/mpris/MediaPlayer2";
    private const string NoTrack = "/org/mpris/MediaPlayer2/TrackList/NoTrack";

    private readonly string _identity;
    private readonly ConcurrentQueue<MprisCommand> _commands = new();
    private MprisState _state = MprisState.Idle;
    private Handler? _handler;
    private DBusConnection? _connection;

    public MprisService(string identity = "Player") => _identity = identity;
    
    #endregion
}