namespace Player.Storage.Database;

/// <summary>One listen that counted. StartedAt is unix seconds; Listened is how long it really played.</summary>
public sealed record HistoryEntry (long StartedAt, string Artist, string Title, string Album, double Duration, double Listened, string TrackId);