namespace Player.Storage.Database;

// The lyrics cache. PlayerDatabase is partial so this lives next to the feature and shares its connection and lock.
public partial class PlayerDatabase
{
    public CachedLyrics? GetLyrics(string trackId)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT kind, source, body, fetched_at FROM lyrics_cache WHERE track_id = $id";
            cmd.Parameters.AddWithValue("$id", trackId);
            using var reader = cmd.ExecuteReader();
            
            return reader.Read()
                ? new CachedLyrics(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3))
                : null;
        }
    }

    /// <summary>Saves (or replaces) the lyrics of a track, stamped with the current time.</summary>
    public void SaveLyrics(string trackId, string kind, string source, string body)
    {
        lock (_gate)
            Run("""
                INSERT INTO lyrics_cache (track_id, kind, source, body, fetched_at)
                VALUES ($id, $kind, $source, $body, $at)
                ON CONFLICT(track_id) DO UPDATE SET kind = $kind, source = $source, body = $body, fetched_at = $at
                """,
                ("$id", trackId), ("$kind", kind), ("$source", source), ("$body", body),
                ("$at", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    }
}