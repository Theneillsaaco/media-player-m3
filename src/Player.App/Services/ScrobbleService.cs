using System.Diagnostics;
using Player.Core.Contracts;
using Player.Core.Domain;
using Player.Scrobble.LastFm;
using Player.Storage.Database;
using Player.Storage.Secrets;

namespace Player.App.Services;

internal enum ScrobbleStage { Unavailable, SignedOut, Authorizing, SignedIn, Reconnect }

internal readonly record struct ScrobbleSnapshot(
    ScrobbleStage Stage, string User, string Status, string AuthUrl, bool Enabled, bool Busy, int Pending);

/// <summary>
/// Sign-in, the on-disk queue of listens and the sending of them. Every method returns at once and
/// the work runs on the thread pool; nothing here raises events, the UI reads <see cref="Snapshot"/>
/// from its poll. Listens are queued first and removed only when the service took them, so a
/// crash, a dead network or an expired session never loses one.
/// </summary>
internal sealed partial class ScrobbleService : IDisposable
{
    private const string Service = "lastfm";
    private const string SessionSecret = "lastfm-session";
    private const string EnabledKey = "lastfm.enabled";
    private const string UserKey = "lastfm.user";
    private const int QueueLimit = 2000;
    private const int BatchSize = 50;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(13); // Last.fm ignores older timestamps

    private IScrobbler? _client;
    private readonly PlayerDatabase _db;
    private readonly SecretStore _secrets;
    private readonly Timer _retry;
    private readonly Lock _gate = new();

    private ScrobbleAccount? _account;
    private SignInRequest? _request;
    private string _user;
    private string _note = "";
    private bool _enabled, _busy, _restoring, _needsReconnect, _flushing, _again;
    private int _pending, _attempt;

    public ScrobbleService(IScrobbler? client, PlayerDatabase db, SecretStore secrets)
    {
        (_client, _db, _secrets) = (client, db, secrets);
        _enabled = db.GetSetting(EnabledKey) == "1";
        _user = db.GetSetting(UserKey) ?? "";
        _pending = db.Count(Service);
        _retry = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public ScrobbleSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                var stage = _client is null ? ScrobbleStage.Unavailable
                    : _account is not null || _restoring ? ScrobbleStage.SignedIn
                    : _request is not null ? ScrobbleStage.Authorizing
                    : _needsReconnect ? ScrobbleStage.Reconnect
                    : ScrobbleStage.SignedOut;
                return new(stage, _user, StatusText(stage), _request?.Url ?? "", _enabled, _busy, _pending);
            }
        }
    }

    /// <summary>Restores the saved session in the background. Call once at startup.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_client is null || _user.Length == 0) return;
            _restoring = true;
        }

        _ = Task.Run(async () =>
        {
            string? key = null;
            try { key = await _secrets.GetAsync(SessionSecret); }
            catch (Exception e) { Log("could not read the session", e); }

            lock (_gate)
            {
                _restoring = false;
                if (string.IsNullOrEmpty(key)) _needsReconnect = true;
                else _account = new ScrobbleAccount(_user, key);
            }
            Flush();
        });
    }

    // ---- sign-in ----

    public void BeginSignIn()
    {
        lock (_gate)
        {
            if (_client is null || _busy) return;
            _busy = true;
            _note = Tr.T("scrobble.asking");
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var request = await _client!.BeginSignIn();
                lock (_gate)
                {
                    _request = request;
                    _note = Tr.T("scrobble.authorize");
                }
                OpenBrowser(request.Url);
            }
            catch (Exception e) { Fail(Tr.T("scrobble.contactFailed"), e); }
            finally { lock (_gate) _busy = false; }
        });
    }

    public void CompleteSignIn()
    {
        SignInRequest request;
        lock (_gate)
        {
            if (_client is null || _busy || _request is null) return;
            request = _request;
            _busy = true;
            _note = Tr.T("scrobble.checking");
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var account = await _client!.CompleteSignIn(request);
                _db.SetSetting(UserKey, account.User);
                _db.SetSetting(EnabledKey, "1");
                lock (_gate)
                {
                    (_account, _request, _user) = (account, null, account.User);
                    (_needsReconnect, _enabled, _attempt, _note) = (false, true, 0, "");
                }
                
                try { await _secrets.Set(SessionSecret, account.SessionKey); }
                catch (Exception e) { Log("could not save the session (it will work until you close the app)", e); }
            }
            catch (LastFmException e) when (e.Code == 14)
            {
                lock (_gate) _note = Tr.T("scrobble.notAuthorized");
            }
            catch (LastFmException e) when (e.Code == 15)
            {
                lock (_gate)
                {
                    _request = null;
                    _note = Tr.T("scrobble.authExpired");
                }
            }
            catch (Exception e) { Fail(Tr.T("scrobble.completeFailed"), e); }
            finally { lock (_gate) _busy = false; }
            Flush();
        });
    }

    public void CancelSignIn()
    {
        lock (_gate)
        {
            if (_busy) return;
            (_request, _note) = (null, "");
        }
    }

    /// <summary>Forgets the account. Listens still waiting stay queued for the next sign-in.</summary>
    public void Disconnect()
    {
        lock (_gate)
        {
            (_account, _request, _user) = (null, null, "");
            (_needsReconnect, _enabled, _note) = (false, false, "");
        }
        _retry.Change(Timeout.Infinite, Timeout.Infinite);
        _db.SetSetting(EnabledKey, "0");
        _db.RemoveSetting(UserKey);
        _ = Task.Run(async () =>
        {
            try { await _secrets.Delete(SessionSecret); }
            catch (Exception e) { Log("could not delete the session", e); }
        });
    }

    public void SetEnabled(bool on)
    {
        lock (_gate) _enabled = on;
        _db.SetSetting(EnabledKey, on ? "1" : "0");
        if (on) Flush();
        else _retry.Change(Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Sends what is waiting now instead of at the next scheduled retry.</summary>
    public void RetryNow()
    {
        lock (_gate) _attempt = 0;
        Flush();
    }

    // ---- listening ----

    public void NowPlaying(Listen listen)
    {
        ScrobbleAccount? account;
        lock (_gate) account = _enabled ? _account : null;
        if (account is null) return;

        _ = Task.Run(async () =>
        {
            try { await _client!.NowPlaying(account, listen); }
            catch (Exception e) { Log("now playing failed", e); }
        });
    }

    /// <summary>Queues a listen that counted and sends it as soon as possible.</summary>
    public void Submit(Listen listen)
    {
        lock (_gate)
        {
            if (_client is null || !_enabled) return;
            if (_account is null && !_restoring && !_needsReconnect) return; // nobody signed in
        }

        _db.Enqueue(Service, listen, QueueLimit);
        RefreshPending();
        Flush();
    }

    // ---- sending ----

    private void Flush()
    {
        lock (_gate)
        {
            if (_client is null || !_enabled || _account is null) return;
            if (_flushing) { _again = true; return; }
            _flushing = true;
        }
        _ = Task.Run(FlushAsync);
    }

    private async Task FlushAsync()
    {
        try
        {
            while (true)
            {
                await DrainAsync();
                lock (_gate)
                {
                    if (!_again) { _flushing = false; return; }
                    _again = false;
                }
            }
        }
        catch (Exception e)
        {
            Log("flush failed", e);
            lock (_gate) (_flushing, _again) = (false, false);
        }
    }

    /// <summary>Sends batches until the queue is empty or something says to stop.</summary>
    private async Task DrainAsync()
    {
        while (true)
        {
            ScrobbleAccount account;
            lock (_gate)
            {
                if (_account is not { } current || !_enabled) return;
                account = current;
            }

            _db.DeleteOlderThan(Service, DateTimeOffset.UtcNow.Subtract(MaxAge).ToUnixTimeSeconds());
            var batch = _db.Peek(Service, BatchSize);
            RefreshPending();
            if (batch.Count == 0)
            {
                lock (_gate) (_attempt, _note) = (0, "");
                return;
            }

            var result = await _client!.Submit(account, [.. batch.Select(b => b.Listen)]);
            switch (result.Outcome)
            {
                case SubmitOutcome.Accepted:
                    _db.Remove(batch.Select(b => b.Id));
                    RefreshPending();
                    lock (_gate) (_attempt, _note) = (0, "");
                    break;

                case SubmitOutcome.Rejected:
                    Log($"dropped {batch.Count} listens: {result.Message}", null);
                    _db.Remove(batch.Select(b => b.Id));
                    RefreshPending();
                    break;

                case SubmitOutcome.Retry:
                    ScheduleRetry(result.Message);
                    return;

                case SubmitOutcome.AuthLost:
                    lock (_gate)
                    {
                        (_account, _needsReconnect) = (null, true);
                        _note = Tr.T("scrobble.sessionLost");
                    }
                    try { await _secrets.Delete(SessionSecret); }
                    catch (Exception e) { Log("could not delete the session", e); }
                    return;

                default: // Fatal
                    lock (_gate) _note = Tr.F("scrobble.rejected", result.Message);
                    return;
            }
        }
    }

    private void ScheduleRetry(string reason)
    {
        TimeSpan delay;
        lock (_gate)
        {
            _attempt++;
            delay = TimeSpan.FromSeconds(Math.Min(60 * Math.Pow(2, _attempt - 1), 900)); // 1, 2, 4 … 15 min
            var wait = delay.TotalMinutes >= 1 ? $"{delay.TotalMinutes:0} min" : $"{delay.TotalSeconds:0} s";
            _note = Tr.F("scrobble.retrying", reason, wait);
        }
        _retry.Change(delay, Timeout.InfiniteTimeSpan);
    }

    private void RefreshPending()
    {
        var count = _db.Count(Service);
        lock (_gate) _pending = count;
    }

    // ---- helpers ----

    private string StatusText(ScrobbleStage stage)
    {
        if (_note.Length > 0) return _note;
        var waiting = _pending switch { 0 => "", 1 => Tr.T("scrobble.pending.one"), var n => Tr.F("scrobble.pending.many", n) };
        return stage switch
        {
            ScrobbleStage.Unavailable => Tr.T("scrobble.missingKey"),
            ScrobbleStage.SignedIn when _account is null => Tr.T("status.connecting"),
            ScrobbleStage.SignedIn => (_enabled ? Tr.F("account.connectedAs", _user) : Tr.F("scrobble.connectedOff", _user)) + waiting,
            ScrobbleStage.Reconnect => Tr.T("scrobble.reconnect") + waiting,
            _ => Tr.T("account.signedOut") + waiting,
        };
    }

    private void Fail(string what, Exception e)
    {
        Log(what, e);
        lock (_gate) _note = $"{what}: {e.Message}";
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { url }, UseShellExecute = false });
        }
        catch (Exception e) { Log("could not open the browser", e); }
    }

    private static void Log(string message, Exception? e) =>
        Console.Error.WriteLine("[scrobble] " + message + (e is null ? "" : ": " + e.Message));

    public void Dispose()
    {
        _retry.Dispose();
        _client?.Dispose();
    }
}
