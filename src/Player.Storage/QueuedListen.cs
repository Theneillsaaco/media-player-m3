using Player.Core;

namespace Player.Storage;

/// <summary>A listen waiting to be sent, with the row id used to remove it once sent.</summary>
public sealed record QueuedListen(long Id, Listen Listen);