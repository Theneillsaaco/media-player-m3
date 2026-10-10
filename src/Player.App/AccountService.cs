using Player.Core;
using Player.Storage;

namespace Player.App;

internal enum AccountStage { Unavailable, SignedOut, SignedIn, Expired }

internal readonly record struct AccountSnapshot(AccountStage Stage, string Name, string Status, bool Busy);

/// <summary>
/// The user's Google session for YouTube Music: importing it from a browser (or pasting the
/// cookie), keeping it in the keyring, and running library calls with one silent re-import when
/// Google rotates the cookie. Every method returns at once or hands back a task; nothing here
/// raises events, the UI reads <see cref="Snapshot"/> from its poll.
/// </summary>
internal sealed class AccountService(IAccountProvider? provider, PlayerDatabase db, SecretStore secrets)
{
    private const string CookieSecret = "ytm-cookie";
    private const string NameKey = "ytm.name";
    private const string BrowserKey = "ytm.browser";
    private const string ProfileKey = "ytm.profile";

    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _reimport = new(1, 1);
    private string _name = db.GetSetting(NameKey) ?? "";
    private string _note = "";
    private bool _busy, _signedIn, _expired;

    public AccountSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                var stage = provider is null ? AccountStage.Unavailable
                    : _signedIn ? AccountStage.SignedIn
                    : _expired ? AccountStage.Expired
                    : AccountStage.SignedOut;
                var status = _note.Length > 0 ? _note
                    : stage switch
                    {
                        AccountStage.Unavailable => "La búsqueda no está disponible, así que tampoco la cuenta.",
                        AccountStage.SignedIn => $"Conectado como {_name}",
                        AccountStage.Expired => "La sesión caducó. Vuelve a importarla.",
                        _ => "Sin conectar",
                    };
                return new(stage, _name, status, _busy);
            }
        }
    }

    /// <summary>Checks the saved session in the background. Call once at startup.</summary>
    public void Start()
    {
        if (provider is null || _name.Length == 0) return; // never signed in
        if (!Begin("Conectando…")) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var cookie = await secrets.GetAsync(CookieSecret);
                if (string.IsNullOrEmpty(cookie)) { MarkExpired(); return; }

                try { Accept(await provider.SignIn(cookie)); }
                catch (AuthExpiredException)
                {
                    if (!await TryReimportAsync()) MarkExpired();
                }
                catch (Exception e) // no network: keep the session, calls will retry later
                {
                    Log("could not check the session", e);
                    lock (_gate) (_signedIn, _note) = (true, "Sin conexión con YouTube Music");
                }
            }
            catch (Exception e) { Fail("No se pudo leer la sesión guardada", e); }
            finally { End(); }
        });
    }

    /// <summary>Reads the session of a browser profile (yt-dlp's cookie extractor in the sidecar).</summary>
    public void ImportBrowser(string browser)
    {
        if (provider is null || !Begin($"Leyendo la sesión de {browser}…")) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var session = await provider.ImportBrowser(browser, "");
                await StoreAsync(session, browser, "");
            }
            catch (Exception e) { Fail("No se pudo importar la sesión", e); }
            finally { End(); }
        });
    }

    /// <summary>Signs in with a pasted cookie header (or the whole request headers copied from DevTools).</summary>
    public void SignInWithCookie(string text)
    {
        if (provider is null) return;
        if (ExtractCookie(text) is not { } cookie)
        {
            lock (_gate) _note = "Eso no parece una cookie de YouTube Music: debe incluir __Secure-3PAPISID.";
            return;
        }
        if (!Begin("Comprobando la cookie…")) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var session = await provider.SignIn(cookie);
                await StoreAsync(session, "", "");
            }
            catch (Exception e) { Fail("No se pudo iniciar sesión", e); }
            finally { End(); }
        });
    }

    public void SignOut()
    {
        if (provider is null) return;
        lock (_gate) (_signedIn, _expired, _name, _note) = (false, false, "", "");
        db.RemoveSetting(NameKey);
        db.RemoveSetting(BrowserKey);
        db.RemoveSetting(ProfileKey);
        _ = Task.Run(async () =>
        {
            try
            {
                await provider.SignOut();
                await secrets.Delete(CookieSecret);
                db.ClearTrackCache(); // the cached lists belong to that account
            }
            catch (Exception e) { Log("could not sign out cleanly", e); }
        });
    }

    // ---- library calls ----

    public Task<IReadOnlyList<PlaylistInfo>> GetLibraryAsync() => RunAsync(() => provider!.GetLibrary());

    public Task<IReadOnlyList<Track>> GetPlaylistAsync(string id) => RunAsync(() => provider!.GetPlaylist(id, 0));

    public Task<bool> RateAsync(string id, bool liked) =>
        RunAsync(async () => { await provider!.Rate(id, liked); return true; });

    /// <summary>Runs a call; if Google rejects the session, re-imports it from the browser once and retries.</summary>
    private async Task<T> RunAsync<T>(Func<Task<T>> call)
    {
        try { return await call(); }
        catch (AuthExpiredException) { }

        try
        {
            if (await TryReimportAsync()) return await call();
        }
        catch (AuthExpiredException) { }

        MarkExpired();
        throw new AuthExpiredException("La sesión de YouTube Music caducó. Vuelve a importarla en Ajustes.");
    }

    /// <summary>Silently repeats the last browser import. False when the session did not come from a browser.</summary>
    private async Task<bool> TryReimportAsync()
    {
        var browser = db.GetSetting(BrowserKey);
        if (provider is null || string.IsNullOrEmpty(browser)) return false;

        await _reimport.WaitAsync();
        try
        {
            var profile = db.GetSetting(ProfileKey) ?? "";
            await StoreAsync(await provider.ImportBrowser(browser, profile), browser, profile);
            return true;
        }
        catch (Exception e)
        {
            Log("silent re-import failed", e);
            return false;
        }
        finally { _reimport.Release(); }
    }

    // ---- helpers ----

    private async Task StoreAsync(AccountSession session, string browser, string profile)
    {
        await secrets.Set(CookieSecret, session.Cookie);
        db.SetSetting(BrowserKey, browser);
        db.SetSetting(ProfileKey, profile);
        Accept(session);
    }

    private void Accept(AccountSession session)
    {
        db.SetSetting(NameKey, session.Name);
        lock (_gate) (_signedIn, _expired, _name, _note) = (true, false, session.Name, "");
    }

    private bool Begin(string note)
    {
        lock (_gate)
        {
            if (_busy) return false;
            (_busy, _note) = (true, note);
            return true;
        }
    }

    private void End()
    {
        lock (_gate) _busy = false;
    }

    private void MarkExpired()
    {
        lock (_gate) (_signedIn, _expired, _note) = (false, true, "");
    }

    private void Fail(string what, Exception e)
    {
        Log(what, e);
        lock (_gate) _note = $"{what}: {e.Message}";
    }

    /// <summary>The cookie header out of whatever was pasted, or null when it lacks the key cookie.</summary>
    internal static string? ExtractCookie(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var header = lines.FirstOrDefault(l => l.StartsWith("cookie:", StringComparison.OrdinalIgnoreCase));
        var cookie = header is not null ? header["cookie:".Length..] : string.Join(" ", lines);
        cookie = string.Join(' ', cookie.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return cookie.Contains("__Secure-3PAPISID=", StringComparison.Ordinal) ? cookie : null;
    }

    // Never log the cookie: only our own messages and exception texts, which do not contain it.
    private static void Log(string message, Exception e) =>
        Console.Error.WriteLine("[account] " + message + ": " + e.Message);
}
