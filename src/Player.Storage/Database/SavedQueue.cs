using Player.Core.Domain;

namespace Player.Storage.Database;

/// <summary>The queue as it was when the player closed. Index is the current track's place in Items.</summary>
public sealed record SavedQueue(IReadOnlyList<Track> Items, int Index, double Seconds, int Volume, string Repeat, bool Shuffle);