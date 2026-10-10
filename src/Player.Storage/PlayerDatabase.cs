using System.Text.Json;
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
    
    #region Scrobble queue

    /// <summary>Adds a listen, then keeps only the newest <paramref name="keep"/> rows of that service.</summary>
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

    #region Saved queue

    private sealed record TrackDto(string Id, string Title, string Artist, string[] Artists, string Album, double Seconds, string Art, string Kind);

    /// <summary>Stores the whole queue (insertion order; the shuffle order is not kept, it is rebuilt).</summary>
    public void SaveQueueState(IReadOnlyList<Track> items, int index, double seconds, int volume, string repeat, bool shuffle)
    {
        var json = JsonSerializer.Serialize(items.Select(t => new TrackDto(
            t.Id, t.Title, t.Artist, t.Artists ?? [], t.Album, t.DurationSeconds, t.ArtUrl,
            t.Kind.IsVideo ? "video" : "song")));
        
        lock (_gate)
            Run("""
                INSERT INTO queue_state (id, items, position, seconds, volume, repeat_mode, shuffle)
                VALUES (1, $items, $pos, $sec, $vol, $rep, $shuf)
                ON CONFLICT(id) DO UPDATE SET items = $items, position = $pos, seconds = $sec,
                    volume = $vol, repeat_mode = $rep, shuffle = $shuf
                """,
                ("$items", json), ("$pos", index), ("$sec", seconds), ("$vol", volume),
                ("$rep", repeat), ("$shuf", shuffle ? 1 : 0));
    }
    
    /// <summary>Cheap update of where the listening is, for when only the position or volume moved.</summary>
    public void SaveProgress(double seconds, int volume)
    {
        lock (_gate)
            Run("UPDATE queue_state SET seconds = $sec, volume = $vol WHERE id = 1", ("$sec", seconds),
                ("$vol", volume));
    }
    
    public SavedQueue? LoadQueueState()
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT items, position, seconds, volume, repeat_mode, shuffle FROM queue_state WHERE id = 1";
            using var reader = cmd.ExecuteReader();
            
            if (!reader.Read())
                return null;
            
            var dtos = JsonSerializer.Deserialize<TrackDto[]>(reader.GetString(0)) ?? [];
            IReadOnlyList<Track> items =
            [
                .. dtos.Select(d => new Track(d.Id, d.Title, d.Artist, d.Artists ?? [], d.Album, d.Seconds, d.Art,
                    d.Kind == "video" ? TrackKind.Video : TrackKind.Song))
            ];

            return new SavedQueue(items, reader.GetInt32(1), reader.GetDouble(2), reader.GetInt32(3),
                reader.GetString(4), reader.GetInt32(5) != 0);
        }
    }

    #endregion
    
    #region playlist cache

    /// <summary>Replaces the cached tracks of a playlist.</summary>
    public void SaveTracks(string listId, IReadOnlyList<Track> tracks)
    {
        lock (_gate)
        {
            using var tx = _db.BeginTransaction();
            Run("DELETE FROM track_cache WHERE list_id = $l", ("$l", listId));

            for (var i = 0; i < tracks.Count; i++)
            {
                var t = tracks[i];
                Run("""
                    INSERT INTO track_cache (list_id, position, id, title, artist, artists, album, seconds, art, kind)
                    VALUES ($l, $p, $id, $title, $artist, $artists, $album, $seconds, $art, $kind)
                    """,
                    ("$l", listId), ("$p", i), ("$id", t.Id), ("$title", t.Title), ("$artist", t.Artist),
                    ("$artists", JsonSerializer.Serialize(t.Artists ?? [])), ("$album", t.Album),
                    ("$seconds", t.DurationSeconds), ("$art", t.ArtUrl),
                    ("$kind", t.Kind.IsVideo ? "video" : "song"));
            }
            
            tx.Commit();
        }
    }

    public IReadOnlyList<Track> LoadTracks(string listId)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                              SELECT id, title, artist, artists, album, seconds, art, kind
                              FROM track_cache WHERE list_id = $l ORDER BY position
                              """;
            cmd.Parameters.AddWithValue("$l", listId);
            using var reader = cmd.ExecuteReader();
            List<Track> rows = [];
            
            while (reader.Read())
                rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    JsonSerializer.Deserialize<string[]>(reader.GetString(3)) ?? Array.Empty<string>(),
                    reader.GetString(4), reader.GetDouble(5), reader.GetString(6),
                    reader.GetString(7) == "video" ? TrackKind.Video : TrackKind.Song));
            
            return rows;
        }
    }

    /// <summary>Forgets every cached playlist (used when the account signs out).</summary>
    public void ClearTrackCache()
    {
        lock (_gate)
            Run("DELETE FROM track_cache");
    }

    #endregion
    
    
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
        """
        CREATE TABLE track_cache (
            list_id  TEXT NOT NULL,
            position INTEGER NOT NULL,
            id       TEXT NOT NULL,
            title    TEXT NOT NULL,
            artist   TEXT NOT NULL,
            artists  TEXT NOT NULL,
            album    TEXT NOT NULL,
            seconds  REAL NOT NULL,
            art      TEXT NOT NULL,
            kind     TEXT NOT NULL,
            PRIMARY KEY (list_id, position)
        ) WITHOUT ROWID;
        """
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