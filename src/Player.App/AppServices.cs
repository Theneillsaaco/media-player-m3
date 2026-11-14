using Player.App.Services;
using Player.Audio.Mpv;
using Player.Core;
using Player.Core.Contracts;
using Player.Core.Localization;
using Player.Providers.Lyrics;
using Player.Providers.YoutubeMusic;
using Player.Scrobble.LastFm;
using Player.Storage;
using Player.Storage.Cache;
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

            MemProbe.StartIfAsked();

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

            // Every service below reads and writes its settings through this: no database, or a failing one,
            // just means defaults.
            Settings = new SafeSettings(Db);

            Theme = new ThemeService(Settings);

            var netease = new NetEaseClient();
            Lyrics = new LyricsService(
                [.. new ILyricsSource?[] { Provider, new LrclibClient(), netease, new KugouClient() }.OfType<ILyricsSource>()],
                id => Db?.GetLyrics(id),
                (id, kind, source, body) => Db?.SaveLyrics(id, kind, source, body),
                Settings.Get,
                Settings.Set,
                Settings.Remove,
                romajiSources: [netease],
                translator: new GoogleTranslateClient());

            Video = new VideoService(Provider, Settings.Get, Settings.Set);
            Prefs = new PlaybackPrefs(Settings);

            // If the cache folder cannot be made (permissions, read-only disk) the app goes on without it.
            try { Cache = new AudioCache(AppPaths.CacheDir("audio"), Prefs.CacheLimit); }
            catch (Exception ex) { Log.Warn("app", "audio cache unavailable", ex); }

            AutoplayFeed = new AutoplayService(Provider);
            LoudnessLookup = new LoudnessService(Provider, () => Account?.SignedIn == true);
            Prefetcher = new PrefetchService(Provider);
            Stats = new StatsService(Provider);
            Playback = new PlaybackService(Engine, Db, Scrobble, Prefetcher, Status, Prefs, AutoplayFeed, LoudnessLookup, Account, Cache);
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
        Settings.Set(I18n.settingKey, value);

        Status.Refresh();
        LanguageChanged?.Invoke();
    }

    private static void Shutdown()
    {
        Playback.Shutdown();
        Cache?.Dispose();
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
    /// <summary>The settings table, safe to use even when the database could not be opened.</summary>
    public static SafeSettings Settings { get; private set; } = new(null);
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
    /// <summary>The songs saved on the disk. Null when the folder could not be made.</summary>
    public static AudioCache? Cache { get; private set; }
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