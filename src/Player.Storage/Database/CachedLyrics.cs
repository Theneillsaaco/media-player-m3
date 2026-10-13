namespace Player.Storage.Database;

/// <summary>
/// Lyrics saved for a track. Kind is "lrc" (timed), "plain", "instrumental" or "none" (looked for, nothing
/// found); Body is the text, empty for the last two. FetchedAt is unix seconds.
/// </summary>
public sealed record CachedLyrics(string Kind, string Source, string Body, long FetchedAt);
