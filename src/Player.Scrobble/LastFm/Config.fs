namespace Player.Scrobble.LastFm

open System
open System.IO
open System.Text.Json

type LastFmCredentials = {
    ApiKey: string
    Secret: string
}

// The API key and secret come from the environment or from a JSON file kept out of the repo:
/// PLAYER_LASTFM_KEY / PLAYER_LASTFM_SECRET, or {"apiKey": "...", "secret": "..."} in the given file.
module LastFmConfig =
    let load (file: string) : LastFmCredentials voption =
        let env name = (Environment.GetEnvironmentVariable name |> Option.ofObj |> Option.defaultValue "").Trim()
        let key, secret = env "PLAYER_LASTFM_KEY", env "PLAYER_LASTFM_SECRET"

        if key <> "" && secret <> "" then
            ValueSome { ApiKey = key; Secret = secret }
        elif File.Exists file then
            try
                use doc = JsonDocument.Parse(File.ReadAllText file)

                let text (name: string) =
                    match doc.RootElement.TryGetProperty(name) with
                    | true, v when v.ValueKind = JsonValueKind.String -> v.GetString().Trim()
                    | _ -> ""

                let key, secret = text "apiKey", text "secret"
                if key <> "" && secret <> "" then
                    ValueSome { ApiKey = key; Secret = secret }
                else ValueNone
            with _ ->
                ValueNone
        else
            ValueNone