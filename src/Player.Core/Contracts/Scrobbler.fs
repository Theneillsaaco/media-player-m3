namespace Player.Core.Contracts

open System
open System.Collections.Generic
open System.Threading.Tasks
open Player.Core.Domain

/// What happened to a batch of listens.
type SubmitOutcome =
    /// The service took the batch (it may have ignored individual listens it found invalid).
    | Accepted = 0
    /// Network trouble, rate limit or a server error: keep the batch and try again later.
    | Retry = 1
    /// The service refused the batch for good: drop it.
    | Rejected = 2
    /// The session is no longer valid: keep the batch and ask the user to sign in again.
    | AuthLost = 3
    /// Misconfiguration (bad API key or signature): keep the batch, stop trying, show the message.
    | Fatal = 4
    
type SubmitResult = {
    Outcome: SubmitOutcome
    Message: string
}

/// A signed-in user of a scrobbling service.
type ScrobbleAccount = {
    User: string
    SessionKey: string
}

/// A sign-in that is waiting for the user to approve it in the browser.
type SignInRequest = {
    Token: string
    Url: string
}

/// A scrobbling service (Last.fm today). Methods run on any thread. Sign-in and submit
/// failures other than the outcomes above surface as exceptions.
type IScrobbler =
    inherit IDisposable
    abstract Name: string
    /// Step one of sign-in: ask the service for a token and the URL the user must open.
    abstract BeginSignIn: unit -> Task<SignInRequest>
    /// Step two, after the user approved the request in the browser.
    abstract CompleteSignIn: request: SignInRequest -> Task<ScrobbleAccount>
    /// "Now playing" courtesy call. Callers ignore its failure.
    abstract NowPlaying: account: ScrobbleAccount * listen: Listen -> Task
    /// Sends listens, oldest first (the service accepts up to 50 per call).
    abstract Submit: account: ScrobbleAccount * listens: IReadOnlyList<Listen> -> Task<SubmitResult>

