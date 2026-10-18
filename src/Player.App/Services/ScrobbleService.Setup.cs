using Player.Core.Contracts;
using Player.Scrobble.LastFm;
using Player.Storage;

namespace Player.App.Services;

// The API key of Last.fm, asked for in Settings instead of by environment variable. ScrobbleService is partial so
// this lives next to the feature and shares its lock and its notes.
internal sealed partial class ScrobbleService
{
    /// <summary>
    /// Saves the API key and shared secret to the config file and starts using them without a restart (an
    /// already saved session is restored). False, with the reason in the status line, when it could not.
    /// </summary>
    public bool Configure(string apiKey, string secret)
    {
        apiKey = apiKey.Trim();
        secret = secret.Trim();

        if (apiKey.Length == 0 || secret.Length == 0)
        {
            lock (_gate) _note = Tr.T("scrobble.enterKeys");
            return false;
        }

        var credentials = new LastFmCredentials(apiKey, secret);

        try
        {
            LastFmConfig.save(AppPaths.ConfigFile("lastfm.json"), credentials);
        }
        catch (Exception e)
        {
            Fail(Tr.T("scrobble.saveKeyFailed"), e);
            return false;
        }

        IScrobbler? old;
        lock (_gate)
        {
            old = _client;
            _client = new LastFmClient(credentials);
            _note = "";
        }

        old?.Dispose();
        Start();
        return true;
    }
}
