namespace Player.Core

open System
open System.Collections.Generic
open System.Threading.Tasks

/// Everything the UI layer needs from an audio backend. No events on purpose: the UI
/// polls on its own thread, so nothing here ever calls back from a foreign thread.
type IAudioEngine =
    inherit IDisposable
    abstract Position: float
    abstract Duration: float
    abstract Paused: bool
    
    /// True when nothing is loaded or playback has finished.
    abstract Idle: bool
    
    /// Starts playing a file path, stream URL or YouTube link.
    abstract Load: source: string -> unit
    abstract SetPaused: paused: bool -> unit
    abstract Seek: seconds: float -> unit
    abstract SetVolume: percent: int -> unit
    /// Drains pending engine events. Returns the error message if playback failed, otherwise "".
    abstract DrainEvents: unit -> string

/// A catalog backend (YouTube Music today). Methods run on any thread and fail the
/// task with an exception; callers must not touch UI state from the continuation.
type IMusicProvider =
    inherit IDisposable
    /// Songs matching the query.
    abstract Search: query: string -> Task<IReadOnlyList<Track>>
    /// Metadata for one track id.
    abstract GetTrack: id: string -> Task<Track>
    
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

/// A playlist in the user's library. Id "LM" is Liked Music, the list YouTube Music keeps from likes.
type PlaylistInfo = {
    Id: string
    Title: string
    Count: int
    ArtUrl: string
}

/// A working Google session. Cookie is the request header value and is as secret as a password.
type AccountSession = {
    Cookie: string
    Name: string
}

/// Google rejected the session (missing, expired or signed out).
type AuthExpiredException(message: string) =
    inherit Exception(message)
    
/// The user's YouTube Music account. Methods run on any thread. Anything that needs the
/// session fails with AuthExpiredException when Google no longer accepts it.
type IAccountProvider =
    inherit IDisposable
    /// Uses this cookie header from now on and returns the account it belongs to.
    abstract SignIn: cookie: string -> Task<AccountSession>
    /// Reads the session of a browser's profile (profile "" = the default one).
    abstract ImportBrowser: browser: string * profile: string -> Task<AccountSession>
    abstract SignOut: unit -> Task
    abstract GetLibrary: unit -> Task<IReadOnlyList<PlaylistInfo>>
    /// Every track of a playlist when limit is 0, otherwise the first `limit`.
    abstract GetPlaylist: id: string * limit: int -> Task<IReadOnlyList<Track>>
    abstract Rate: id: string * liked: bool -> Task