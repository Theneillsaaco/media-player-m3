namespace Player.Storage.Database;

/// <summary>
/// Schema history. Index + 1 is the version stored in PRAGMA user_version. Append, never edit.
/// </summary>
internal static class Migrations
{
    public static readonly string[] All =
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
}