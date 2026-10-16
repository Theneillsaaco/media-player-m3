namespace Player.Scrobble.LastFm

open System
open System.Collections.Generic
open System.IO
open System.Text
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
            
    /// Writes {"apiKey": ..., "secret": ...} to the file, readable by its owner only (the secret is as private as a
    /// password). The folder is created with mode 700 when it is new.
    let save (file: string) (credentials: LastFmCredentials) : unit =
        match Path.GetDirectoryName file with
        | null
        | "" -> ()
        | dir ->
            Directory.CreateDirectory(
                dir,
                UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute
            )
            |> ignore

        if File.Exists file then
            File.Delete file

        let values = Dictionary<string, string>()
        values["apiKey"] <- credentials.ApiKey
        values["secret"] <- credentials.Secret

        let options =
            FileStreamOptions(
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                UnixCreateMode = Nullable(UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            )

        use stream = new FileStream(file, options)
        let bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize values)
        stream.Write(bytes, 0, bytes.Length)
