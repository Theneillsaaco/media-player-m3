namespace Player.Storage.Database;

// The listening history. PlayerDatabase is partial so this lives next to the feature and shares its connection and lock.
public partial class PlayerDatabase
{
    /// <summary>The newest listens first.</summary>
    public IReadOnlyList<HistoryEntry> GetHistory(int limit)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                              SELECT started_at, artist, title, album, duration, listened, track_id
                              FROM history ORDER BY started_at DESC, id DESC LIMIT $n
                              """;
            cmd.Parameters.AddWithValue("$n", limit);

            using var reader = cmd.ExecuteReader();
            List<HistoryEntry> rows = [];
            
            while (reader.Read())
                rows.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetDouble(4), reader.GetDouble(5), reader.GetString(6)));
            
            return rows;
        }
    }

    public void ClearHistory()
    {
        lock (_gate)
            Run("DELETE FROM history");
    }
}