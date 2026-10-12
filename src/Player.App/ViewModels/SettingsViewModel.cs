using System.ComponentModel;
using System.Runtime.CompilerServices;
using Player.App.Services;
using Player.Core;
using Player.Core.Playback;

namespace Player.App.ViewModels;

/// <summary>The settings dialog: startup behaviour, YouTube Music account and Last.fm connection (state, status, commands).</summary>
public class SettingsViewModel : INotifyPropertyChanged
{
    public SettingsViewModel()
    {
        AppServices.EnsureStarted();
        ScrobbleStatus = AppServices.DatabaseError;
        Startup = ReadStartup();
        SyncScrobbler();
        SyncAccount();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>What happens to the saved session at start: "continue", "fromStart" (default) or "none".</summary>
    public string Startup { get; private set => Set(ref field, value); } = "fromStart";

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
    }

    public void ChooseStartup(string mode)
    {
        var value = Resume.toSetting(Resume.parse(mode));
        Startup = value;

        try { AppServices.Db?.SetSetting(Resume.settingKey, value); }
        catch (Exception e) { Log.Warn("settings", "startup mode", e); }
    }

    public void ImportBrowserSession(string browser) { AppServices.Account?.ImportBrowser(browser); AccountChangedByUser(); }
    public void UseCookie(string text) { AppServices.Account?.SignInWithCookie(text); AccountChangedByUser(); }
    public void SignOutAccount() { AppServices.Account?.SignOut(); AccountChangedByUser(); }

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
