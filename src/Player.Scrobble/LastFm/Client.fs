namespace Player.Scrobble.LastFm

open System
open System.Collections.Generic
open System.IO
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
open Player.Core.Contracts
open Player.Core.Domain
open Player.Core.Localization

type LastFmException(code: int, message: string) =
    inherit Exception(message)
    member _.Code = code

module private Wire =
    let endpoint = Uri "https://ws.audioscrobbler.com/2.0/"

    let classify (code: int) =
        match code with
        | 9 -> SubmitOutcome.AuthLost
        | 8 | 11 | 16 | 29 -> SubmitOutcome.Retry
        | 4 | 10 | 13 | 26 -> SubmitOutcome.Fatal
        | _ -> SubmitOutcome.Rejected

    let failure (ex: exn) : SubmitResult =
        match ex with
        | :? LastFmException as e -> { Outcome = classify e.Code; Message = e.Message }
        | :? HttpRequestException
        | :? TaskCanceledException
        | :? IOException -> { Outcome = SubmitOutcome.Retry; Message = I18n.t "lastfm.offline" }
        | _ -> { Outcome = SubmitOutcome.Retry; Message = ex.Message }

    let optional (name: string) (value: string) =
        if String.IsNullOrWhiteSpace value then [] else [ name, value ]

    /// artist, track, album and duration for one listen. The suffix is "" for a single call
    /// and "[n]" inside a batch.
    let listenParams (suffix: string) (l: Listen) =
        [ "artist" + suffix, l.Artist; "track" + suffix, l.Title ]
        @ optional ("album" + suffix) l.Album
        @ (if l.Chosen then [] else [ "chosenByUser" + suffix, "0" ])
        @ (if l.DurationSeconds >= 1.0 then
               [ "duration" + suffix, string (int (Math.Round l.DurationSeconds)) ]
           else
               [])

    let batchParams (listens: IReadOnlyList<Listen>) =
        listens
        |> Seq.indexed
        |> Seq.collect (fun (i, l) ->
            let suffix = $"[{i}]"
            listenParams suffix l @ [ "timestamp" + suffix, string l.StartedAt ])
        |> Seq.toList

    let attr (root: JsonElement) (name: string) =
        try
            root.GetProperty("scrobbles").GetProperty("@attr").GetProperty(name).GetInt32()
        with _ ->
            0

type LastFmClient(credentials: LastFmCredentials) =
    let http =
        let client = new HttpClient(Timeout = TimeSpan.FromSeconds 20.0)
        client.DefaultRequestHeaders.UserAgent.ParseAdd "Player/0.2"
        client

    /// One signed POST. Returns the parsed reply, or raises LastFmException for an error reply.
    let call (methodName: string) (parameters: (string * string) list) =
        task {
            let signed = [ "method", methodName; "api_key", credentials.ApiKey ] @ parameters
            let signature = LastFmSigning.sign credentials.Secret signed

            let form =
                signed @ [ "api_sig", signature; "format", "json" ]
                |> List.map (fun (k, v) -> KeyValuePair(k, v))

            use content = new FormUrlEncodedContent(form)
            use! response = http.PostAsync(Wire.endpoint, content)
            let! body = response.Content.ReadAsStringAsync()

            let parsed =
                try
                    Some(JsonDocument.Parse body)
                with :? JsonException ->
                    None

            match parsed with
            | None ->
                return raise (LastFmException(11, I18n.tf "lastfm.invalid" [| box (int response.StatusCode) |]))
            | Some doc ->
                use doc = doc
                let root = doc.RootElement

                match root.TryGetProperty("error") with
                | true, code ->
                    let message =
                        match root.TryGetProperty("message") with
                        | true, m when m.ValueKind = JsonValueKind.String -> m.GetString()
                        | _ -> $"error {code.GetInt32()}"

                    return raise (LastFmException(code.GetInt32(), message))
                | _ -> return root.Clone()
        }

    interface IScrobbler with
        member _.Name = "Last.fm"

        member _.BeginSignIn() =
            task {
                let! reply = call "auth.getToken" []
                let token = reply.GetProperty("token").GetString()

                let url =
                    $"https://www.last.fm/api/auth/?api_key={Uri.EscapeDataString credentials.ApiKey}&token={Uri.EscapeDataString token}"

                return { Token = token; Url = url }
            }

        member _.CompleteSignIn(request: SignInRequest) =
            task {
                let! reply = call "auth.getSession" [ "token", request.Token ]
                let session = reply.GetProperty "session"

                return
                    { User = session.GetProperty("name").GetString()
                      SessionKey = session.GetProperty("key").GetString() }
            }

        member _.NowPlaying(account: ScrobbleAccount, listen: Listen) =
            task {
                let! _ = call "track.updateNowPlaying" (Wire.listenParams "" listen @ [ "sk", account.SessionKey ])
                ()
            }
            :> Task

        member _.Submit(account: ScrobbleAccount, listens: IReadOnlyList<Listen>) =
            task {
                try
                    let! reply = call "track.scrobble" (Wire.batchParams listens @ [ "sk", account.SessionKey ])
                    let ignored = Wire.attr reply "ignored"

                    return
                        { Outcome = SubmitOutcome.Accepted
                          Message = if ignored > 0 then I18n.tf "lastfm.ignored" [| box ignored |] else "" }
                with ex ->
                    return Wire.failure ex
            }

    interface IDisposable with
        member _.Dispose() = http.Dispose()
