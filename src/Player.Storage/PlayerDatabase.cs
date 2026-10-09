using Microsoft.Data.Sqlite;
using Player.Core;

namespace Player.Storage;

/// <summary>
/// The player's SQLite file: settings, scrobbles waiting to be sent, play history and the saved
/// queue. One connection guarded by a lock, so any thread may call it; every call is short.
/// </summary>
public class PlayerDatabase : IDisposable
{
    public string? GetSetting(string key)
    {
        lock (_gate)
            return Scalar("SELECT value FROM settings WHERE key = $k", ("$k", key)) as string;
    }
    
    public void SetSetting(string key, string value)
    {
        lock (_gate)
            Run("INSERT INTO settings (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = $v",
                ("$k", key), ("$v", value));
    }
    
    public void RemoveSetting(string key)
    {
        lock (_gate)
            Run("DELETE FROM settings WHERE key = $k", ("$k", key));
    }
    
    #region scrobble queue

    public void Enqueue(string service, Listen listen, int keep)
    {
        lock (_gate)
        {
            Run("""
                INSERT INTO scrobble_queue (service, started_at, artist, title, album, duration)
                VALUES ($s, $at, $artist, $title, $album, $duration)
                """,
                ("$s", service), ("$at", listen.StartedAt), ("$artist", listen.Artist),
                ("$title", listen.Title), ("$album", listen.Album), ("$duration", listen.DurationSeconds));
            Run("""
                DELETE FROM scrobble_queue
                WHERE service = $s AND id NOT IN
                    (SELECT id FROM scrobble_queue WHERE service = $s ORDER BY id DESC LIMIT $keep)
                """,
                ("$s", service), ("$keep", keep));

        }
    }
    
    /// <summary>The oldest waiting listens, oldest first.</summary>
    public IReadOnlyList<QueuedListen> Peek(string service, int max)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                              SELECT id, started_at, artist, title, album, duration
                              FROM scrobble_queue WHERE service = $s ORDER BY id LIMIT $n
                              """;

            cmd.Parameters.AddWithValue("$s", service);
            cmd.Parameters.AddWithValue("$n", max);

            using var reader = cmd.ExecuteReader();
            List<QueuedListen> rows = [];
            
            while (reader.Read())
                rows.Add(new(reader.GetInt64(0),
                    new Listen(reader.GetInt64(1), reader.GetString(2), reader.GetString(3), 
                        reader.GetString(4), reader.GetDouble(5))));
            
            return rows;
        }
    }
    
    public int Count(string service)
    {
        lock (_gate)
            return Convert.ToInt32(Scalar("SELECT COUNT(*) FROM scrobble_queue WHERE service = $s", ("$s", service)));
    }
    
    public void Remove(IEnumerable<long> ids)
    {
        lock (_gate)
        {
            using var tx = _db.BeginTransaction();
            
            foreach (var id in ids)
                Run("DELETE FROM scrobble_queue WHERE id = $id", ("$id", id));
            tx.Commit();
        }
    }
    
    /// <summary>Drops listens that started before the cutoff (unix seconds); services ignore them anyway.</summary>
    public void DeleteOlderThan(string service, long cutoff)
    {
        lock (_gate)
            Run("DELETE FROM scrobble_queue WHERE service = $s AND started_at < $c", ("$s", service), ("$c", cutoff));
    }
    
    #endregion
    
    public void AddHistory(Listen listen, double listenedSeconds, string trackId)
    {
        lock (_gate)
            Run("""
                INSERT INTO history (started_at, artist, title, album, duration, listened, track_id)
                VALUES ($at, $artist, $title, $album, $duration, $listened, $id)
                """,
                ("$at", listen.StartedAt), ("$artist", listen.Artist), ("$title", listen.Title),
                ("$album", listen.Album), ("$duration", listen.DurationSeconds),
                ("$listened", listenedSeconds), ("$id", trackId));
    }
    
    public void Dispose()
    {
        lock (_gate) _db.Dispose();
    }

    #region Plumbing

    private void Migrate()
    {
        var version = Convert.ToInt32(Scalar("PRAGMA user_version"));

        for (var v = version; v < Migrations.Length; v++)
        {
            using var tx = _db.BeginTransaction();
            
            Run(Migrations[v]);
            Run($"PRAGMA user_version = {v + 1}"); // PRAGMA takes no parameters; v is our own int
            tx.Commit();
        }
    }
    
    private int Run(string sql, params ReadOnlySpan<(string Name, object? Value)> args)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        
        return cmd.ExecuteNonQuery();
    }
    
    private object? Scalar(string sql, params ReadOnlySpan<(string Name, object? Value)> args)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;

        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        
        return cmd.ExecuteScalar();
    }

    #endregion
    
    #region Fields

    // Index + 1 is the schema version stored in PRAGMA user_version. Append, never edit.
    private static readonly string[] Migrations =
    [
        """
        CREATE TABLE settings (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        ) WITHOUT ROWID;

        CREATE TABLE scrobble_queue (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            service    TEXT NOT NULL,
            started_at INTEGER NOT NULL,
            artist     TEXT NOT NULL,
            title      TEXT NOT NULL,
            album      TEXT NOT NULL DEFAULT '',
            duration   REAL NOT NULL DEFAULT 0
        );
        CREATE INDEX scrobble_queue_service ON scrobble_queue (service, id);

        CREATE TABLE history (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            started_at INTEGER NOT NULL,
            artist     TEXT NOT NULL,
            title      TEXT NOT NULL,
            album      TEXT NOT NULL DEFAULT '',
            duration   REAL NOT NULL DEFAULT 0,
            listened   REAL NOT NULL,
            track_id   TEXT NOT NULL
        );
        CREATE INDEX history_started ON history (started_at);

        CREATE TABLE queue_state (
            id          INTEGER PRIMARY KEY CHECK (id = 1),
            items       TEXT NOT NULL,
            position    INTEGER NOT NULL,
            seconds     REAL NOT NULL,
            volume      INTEGER NOT NULL,
            repeat_mode TEXT NOT NULL,
            shuffle     INTEGER NOT NULL
        );
        """,
    ];

    private readonly Lock _gate = new();
    private readonly SqliteConnection _db;
    
    public static string DefaultPath => AppPaths.DataFile("player.db");

    public PlayerDatabase(string path)
    {
        AppPaths.Prepare(path);
        
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        _db.Open();
        
        Run("PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;");
        Migrate();
    }

    #endregion
}