namespace Player.Mpris;

/// <summary>What the desktop is told about the player. The app builds a new one whenever something changes.</summary>
public sealed record MprisState(
    string Status, // "Playing" | "Paused" | "Stopped"
    string TrackKey, // "" when nothing is current; any string, turned into a D-Bus object path
    string Title,
    IReadOnlyList<string> Artists,
    string Album,
    string ArtUrl,
    long LengthMicros,
    long PositionMicros,
    double Volume, // 0..1
    string Loop, // "None" | "Track" | "Playlist"
    bool Shuffle,
    bool CanGoNext,
    bool CanGoPrevious,
    bool CanPlay,
    bool CanSeek
)
{
    public static MprisState Idle { get; } =
        new("Stopped", "", "", [], "", "", 0, 0, 1.0, "None", false, false, false, false, false);
}
