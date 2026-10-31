namespace Player.Core.Playback

open System

/// What to tell the listener when a track does not play. `reason` maps what yt-dlp, mpv or the network said to the
/// key of a text (see Translations); it is "" when the error is not one of the known ones.
module PlayErrors =
    /// The first rule whose phrase the error holds wins, so the narrower reasons come before the general ones.
    let private rules: (string list * string) list = [
        [ "available in your country"; "blocked it in your country"; "blocked in your country"; "geo restrict"; "geo-restrict" ], "error.region"
        [ "confirm your age"; "age-restricted"; "age restricted"; "inappropriate for some users" ], "error.age"
        [ "not a bot"; "sign in to confirm"; "login required"; "use --cookies" ], "error.signIn"
        [ "private video"; "this video is private" ], "error.private"
        [ "premium"; "members-only"; "members only"; "join this channel" ], "error.premium"
        [ "has been removed"; "video unavailable"; "this video is unavailable"; "no longer available"; "does not exist"; "account associated with this video has been terminated"; "copyright" ], "error.removed"
        [ "requested format is not available"; "no video formats found"; "no playable stream" ], "error.noFormat"
        [ "server returned 403"; "server returned 404"; "server returned 410"; "403 forbidden"; "404 not found"; "410 gone"; "http error 403"; "http error 404"; "http error 410" ], "error.stopped"
        [ "name resolution"; "no such host"; "network is unreachable"; "no route to host"; "timed out"; "timeout"; "deadline exceeded"; "connection reset"; "connection refused"; "connection closed"; "unable to download"; "failed to resolve"; "temporary failure"; "end of file"; "i/o error"; "server returned 5"; "transporterror" ], "error.network"
        [ "invalid data found"; "unsupported"; "no such file"; "audio device" ], "error.unplayable"
    ]

    /// The key of the text that explains the error, or "" when it is not a known one.
    let reason (raw: string) : string =
        if String.IsNullOrWhiteSpace raw then
            ""
        else
            let lower = raw.ToLowerInvariant()

            rules
            |> List.tryFind (fun (phrases, _) -> phrases |> List.exists (fun phrase -> lower.Contains(phrase)))
            |> Option.map snd
            |> Option.defaultValue ""

    /// The first line of an error, for the ones nobody knows.
    let firstLine (raw: string) : string =
        if isNull raw then
            ""
        else
            raw.Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
            |> Array.tryHead
            |> Option.map (fun line -> line.Trim())
            |> Option.defaultValue ""
