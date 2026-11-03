using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Player.App.Services;
using Player.Core;
using Player.Core.Playback;

namespace Player.App.ViewModels;

/// <summary>The settings dialog: startup behaviour, playback, audio cache, YouTube Music account and Last.fm connection (state, status, commands).</summary>
public class SettingsViewModel : INotifyPropertyChanged
{
    public SettingsViewModel()
    {
        AppServices.EnsureStarted();
        ScrobbleStatus = AppServices.DatabaseError;
        Startup = ReadStartup();
        AutoPlay = AppServices.Prefs.AutoPlay;
        NormalizeVolume = AppServices.Prefs.Normalize;
        CacheLimit = AppServices.Cache?.Limit ?? AppServices.Prefs.CacheLimit;
        SyncCache();
        SyncScrobbler();
        SyncAccount();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>What happens to the saved session at start: "continue", "fromStart" (default) or "none".</summary>
    public string Startup { get; private set => Set(ref field, value); } = "fromStart";

    /// <summary>Keep playing similar music when the queue ends.</summary>
    public bool AutoPlay { get; private set => Set(ref field, value); } = true;
    /// <summary>Turn loud songs down to a common level (needs a YouTube Music account).</summary>
    public bool NormalizeVolume { get; private set => Set(ref field, value); } = true;
    
    /// <summary>How many songs the disk cache keeps; 0 is off.</summary>
    public int CacheLimit { get; private set => Set(ref field, value); } = 30;
    /// <summary>"12 canciones · 48 MB", or "Vacía".</summary>
    public string CacheSummary { get; private set => Set(ref field, value); } = "";

    // Last.fm. State is one of: unavailable, signedOut, authorizing, signedIn, reconnect.
    public string ScrobbleState { get; private set => Set(ref field, value); } = "unavailable";
    public string ScrobbleUser { get; private set => Set(ref field, value); } = "";
    public string ScrobbleStatus { get; private set => Set(ref field, value); } = "";
    public string ScrobbleAuthUrl { get; private set => Set(ref field, value); } = "";
    public bool ScrobbleEnabled { get; private set => Set(ref field, value); }
    public bool ScrobbleBusy { get; private set => Set(ref field, value); }
    public int ScrobblePending { get; private set => Set(ref field, value); }

    // YouTube Music account. State is one of: unavailable, signedOut, signedIn, expired.
    public string AccountState { get; private set => Set(ref field, value); } = "unavailable";
    public string AccountName { get; private set => Set(ref field, value); } = "";
    public string AccountStatus { get; private set => Set(ref field, value); } = "";
    public bool AccountBusy { get; private set => Set(ref field, value); }

    /// <summary>True while a sign-in or a connection check is running, so the UI timer keeps polling.</summary>
    public bool Busy { get; private set => Set(ref field, value); }

    /// <summary>Re-reads both services. The dialog and Main.qml call it from a Timer.</summary>
    public void Poll()
    {
        SyncScrobbler();
        SyncAccount();
        SyncCache();
    }

    public void ChooseStartup(string mode)
    {
        var value = Resume.toSetting(Resume.parse(mode));
        Startup = value;

        try { AppServices.Db?.SetSetting(Resume.settingKey, value); }
        catch (Exception e) { Log.Warn("settings", "startup mode", e); }
    }

    public void ChooseAutoPlay(bool on)
    {
        AppServices.Prefs.SaveAutoPlay(on);
        AutoPlay = on;
    }

    public void ChooseNormalizeVolume(bool on)
    {
        AppServices.Prefs.SaveNormalize(on);
        NormalizeVolume = on;
        AppServices.Playback.RefreshSound();
    }

    /// <summary>How many songs to keep on the disk (0 turns the cache off and empties it).</summary>
    public void ChooseCacheLimit(int limit)
    {
        var value = CachePolicy.normalizeLimit(limit);
        AppServices.Prefs.SaveCacheLimit(value);
        AppServices.Cache?.SetLimit(value);
        CacheLimit = value;
        SyncCache();
        AppServices.Playback.RefreshSound(); // asks for what plays next again, now that the cache can take it
    }

    /// <summary>Deletes the songs saved on the disk.</summary>
    public void ClearCache()
    {
        AppServices.Cache?.Clear();
        SyncCache();
    }

    public void ImportBrowserSession(string browser) { AppServices.Account?.ImportBrowser(browser); AccountChangedByUser(); }
    public void UseCookie(string text) { AppServices.Account?.SignInWithCookie(text); AccountChangedByUser(); }
    public void SignOutAccount() { AppServices.Account?.SignOut(); AccountChangedByUser(); }

    /// <summary>Saves the Last.fm API key and secret from the dialog; the Conectar button appears right away.</summary>
    public void SaveScrobbleKeys(string apiKey, string secret) { AppServices.Scrobble?.Configure(apiKey, secret); SyncScrobbler(); }
    public void ConnectScrobbler() { AppServices.Scrobble?.BeginSignIn(); SyncScrobbler(); }
    public void FinishScrobblerSignIn() { AppServices.Scrobble?.CompleteSignIn(); SyncScrobbler(); }
    public void CancelScrobblerSignIn() { AppServices.Scrobble?.CancelSignIn(); SyncScrobbler(); }
    public void DisconnectScrobbler() { AppServices.Scrobble?.Disconnect(); SyncScrobbler(); }
    public void EnableScrobbling(bool on) { AppServices.Scrobble?.SetEnabled(on); SyncScrobbler(); }
    public void RetryScrobbles() { AppServices.Scrobble?.RetryNow(); SyncScrobbler(); }

    private static string ReadStartup()
    {
        try { return Resume.toSetting(Resume.parse(AppServices.Db?.GetSetting(Resume.settingKey))); }
        catch (Exception e)
        {
            Log.Warn("settings", "read startup mode", e);
            return "fromStart";
        }
    }

    private void AccountChangedByUser()
    {
        SyncAccount();
        AppServices.RaiseAccountChanged(); // the library page reacts to it
    }
    
    private void SyncCache()
    {
        if (AppServices.Cache is not { } cache)
        {
            CacheSummary = "";
            return;
        }

        var (files, bytes) = cache.Stats;
        CacheSummary = files == 0 ? Tr.T("cache.empty") : Tr.F("cache.used", files, FormatBytes(bytes));
    }

    private static string FormatBytes(long bytes) => bytes >= 1L << 30
        ? (bytes / (double)(1L << 30)).ToString("0.0", CultureInfo.InvariantCulture) + " GB"
        : (bytes / (double)(1L << 20)).ToString("0", CultureInfo.InvariantCulture) + " MB";
    
    private void SyncScrobbler()
    {
        if (AppServices.Scrobble is not { } scrobble) return;
        var s = scrobble.Snapshot;
        ScrobbleState = AccountView.StateName(s.Stage);
        ScrobbleUser = s.User;
        ScrobbleStatus = s.Status;
        ScrobbleAuthUrl = s.AuthUrl;
        ScrobbleEnabled = s.Enabled;
        ScrobbleBusy = s.Busy;
        ScrobblePending = s.Pending;
        Busy = ScrobbleBusy || AccountBusy;
    }

    private void SyncAccount()
    {
        if (AppServices.Account is not { } account) return;
        var s = account.Snapshot;
        AccountState = AccountView.StateName(s.Stage);
        AccountName = s.Name;
        AccountStatus = s.Status;
        AccountBusy = s.Busy;
        Busy = ScrobbleBusy || AccountBusy;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
