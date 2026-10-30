namespace Player.Audio.Mpv;

internal static class MpvOptions
{
    public static readonly (string Name, string Value)[] Defaults =
    [
        ("config", "no"), ("terminal", "no"), ("idle", "yes"), ("vid", "no"),
        ("audio-display", "no"), ("input-default-bindings", "no"), ("input-vo-keyboard", "no"),
        ("ytdl", "yes"), ("ytdl-format", "bestaudio/best"), ("audio-client-name", "Player"),
        ("ytdl-raw-options", "extractor-args=youtube:player_client=<cliente_de_audio>"),
        ("prefetch-playlist", "yes"), ("gapless-audio", "yes"),
        // The demuxer cache is capped: a song is a few MB, so the 150 MiB mpv allows by default is never needed for
        // audio and only lets the process grow when a long stream or a video is opened.
        ("demuxer-max-bytes", "40Mib"), ("demuxer-max-back-bytes", "8Mib")
    ];
}