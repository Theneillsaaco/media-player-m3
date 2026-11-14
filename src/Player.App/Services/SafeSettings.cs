using Player.Core;
using Player.Storage.Database;

namespace Player.App.Services;

/// <summary>
/// The settings table with every failure swallowed and logged. Reading gives null and writing does nothing when the
/// database is missing or fails, so no caller needs its own try/catch. Pass <see cref="Get"/> and <see cref="Set"/>
/// wherever a service takes read and write functions.
/// </summary>
internal sealed class SafeSettings(PlayerDatabase? db)
{
    public string? Get(string key)
    {
        try { return db?.GetSetting(key); }
        catch (Exception ex)
        {
            Log.Warn("settings", "read " + key, ex);
            return null;
        }
    }

    public void Set(string key, string value)
    {
        try { db?.SetSetting(key, value); }
        catch (Exception ex) { Log.Warn("settings", "save " + key, ex); }
    }
    
    public void Remove(string key)
    {
        try { db?.RemoveSetting(key); }
        catch (Exception ex) { Log.Warn("settings", "remove " + key, ex); }
    }
}