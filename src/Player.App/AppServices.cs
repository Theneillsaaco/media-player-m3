using Player.App.Services;
using Player.Audio.Mpv;
using Player.Core;
using Player.Core.Contracts;
using Player.Core.Localization;
using Player.Providers.Lyrics;
using Player.Providers.YoutubeMusic;
using Player.Scrobble.LastFm;
using Player.Storage;
using Player.Storage.Database;
using Player.Storage.Secrets;

namespace Player.App;

/// <summary>
/// Composition root: owns the engine, the database and the services, and is what every view model
/// reads. Created on first use (<see cref="EnsureStarted"/>, called by each view model's constructor),
/// which happens while QML loads, after Qt is up: the mpv engine must not be created before that.
/// </summary>
internal static class AppServices
{
    public static void EnsureStarted()
    {
        lock (Gate)
        {
            if (_started)
                return;

            _started = true;

            Engine = new MpvEngine();
            
            try { Provider = new YtSidecarProvider(); }
            catch (Exception ex) { Status.Text = Tr.F("app.searchUnavailable", ex.Message); }

            try
            {
                Db = new PlayerDatabase(PlayerDatabase.DefaultPath);
                I18n.select(Db.GetSetting(I18n.settingKey) ?? "es"); // before any text is built
                var secrets = new SecretStore();
                Account = new AccountService(Provider, Db, secrets);
                
                Account.Start();
                IScrobbler? client = LastFmConfig.load(AppPaths.ConfigFile("lastfm.json")) is { IsSome: true } credentials
                    ? new LastFmClient(credentials.Value)
                    : null;
                
                Scrobble = new ScrobbleService(client, Db, secrets);
                Scrobble.Start();
            }
            catch (Exception ex)
            {
                Log.Error("app", "storage unavailable", ex);
                DatabaseError = Tr.F("app.dbFailed", ex.Message);
            }

            Theme = new ThemeService(
                key => { try { return Db?.GetSetting(key); } catch (Exception ex) { Log.Warn("app", "theme setting", ex); return null; } },
                (key, value) => Db?.SetSetting(key, value));

            Lyrics = new LyricsService(
                [.. new ILyricsSource?[] { Provider, new LrclibClient() }.OfType<ILyricsSource>()],
                id => Db?.GetLyrics(id),
                (id, kind, source, body) => Db?.SaveLyrics(id, kind, source, body),
                key => Db?.GetSetting(key),
                (key, value) => Db?.SetSetting(key, value),
                key => Db?.RemoveSetting(key));

            Video = new VideoService(
                Provider,
                key => { try { return Db?.GetSetting(key); } catch (Exception ex) { Log.Warn("app", "video setting", ex); return null; } },
                (key, value) => Db?.SetSetting(key, value));

            Prefs = new PlaybackPrefs(
                key => { try { return Db?.GetSetting(key); } catch (Exception ex) { Log.Warn("app", "playback setting", ex); return null; } },
                (key, value) => Db?.SetSetting(key, value));
            AutoplayFeed = new AutoplayService(Provider);
            LoudnessLookup = new LoudnessService(Provider, () => Account?.SignedIn == true);
            Prefetcher = new PrefetchService(Provider);
            Stats = new StatsService(Provider);
            Playback = new PlaybackService(Engine, Db, Scrobble, Prefetcher, Status, Prefs, AutoplayFeed, LoudnessLookup, Account);
            Playback.Restore();
            Playback.ApplyStartupSound();
            Playback.StartMpris();
            
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();
        }
    }

    /// <summary>Switches the interface language, remembers it and tells whoever shows built texts.</summary>
    public static void ChooseLanguage(string language)
    {
        var value = I18n.normalize(language);
        I18n.select(value);

        try { Db?.SetSetting(I18n.settingKey, value); }
        catch (Exception ex) { Log.Warn("app", "language setting", ex); }

        Status.Refresh();
        LanguageChanged?.Invoke();
    }
    
    private static void Shutdown()
    {
        Playback.Shutdown();
        Scrobble?.Dispose();
        Theme.Dispose();
        Provider?.Dispose();
        Engine.Dispose();
        Db?.Dispose();
    }
    
    #region Fields

    private static readonly Lock Gate = new();
    private static bool _started;

    public static IAudioEngine Engine { get; private set; } = null!;
    public static PlayerDatabase? Db { get; private set; }
    public static YtSidecarProvider? Provider { get; private set; }
    public static AccountService? Account { get; private set; }
    public static ScrobbleService? Scrobble { get; private set; }
    public static ThemeService Theme { get; private set; } = null!;
    public static LyricsService Lyrics { get; private set; } = null!;
    public static VideoService Video { get; private set; } = null!;
    public static PrefetchService Prefetcher { get; private set; } = null!;
    public static StatsService Stats { get; private set; } = null!;
    public static PlaybackPrefs Prefs { get; private set; } = null!;
    public static AutoplayService AutoplayFeed { get; private set; } = null!;
    public static LoudnessService LoudnessLookup { get; private set; } = null!;
    public static PlaybackService Playback { get; private set; } = null!;
    public static StatusLine Status { get; } = new();
    
    /// <summary>Why the database could not be opened, "" when it could. Shown in settings.</summary>
    public static string DatabaseError { get; private set; } = "";

    /// <summary>Raised on the Qt thread after the settings dialog changed the account (sign-in or sign-out).</summary>
    public static event Action? AccountChanged;
    public static void RaiseAccountChanged() => AccountChanged?.Invoke();

    /// <summary>Raised on the Qt thread after the interface language changed.</summary>
    public static event Action? LanguageChanged;

    #endregion
}
