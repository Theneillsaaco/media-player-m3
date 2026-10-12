namespace Player.Audio.Mpv;

internal static class MpvOptions
{
    public static readonly (string Name, string Value)[] Defaults =
    [
        ("config", "no"), ("terminal", "no"), ("idle", "yes"), ("vid", "no"),
        ("audio-display", "no"), ("input-default-bindings", "no"), ("input-vo-keyboard", "no"),
        ("ytdl", "yes"), ("ytdl-format", "bestaudio/best"), ("audio-client-name", "Player"),
        ("prefetch-playlist", "yes"), ("gapless-audio", "yes"),
    ];
}