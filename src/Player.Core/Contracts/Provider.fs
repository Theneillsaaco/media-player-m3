namespace Player.Core.Contracts

open System
open System.Collections.Generic
open System.Threading.Tasks
open Player.Core.Domain

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

/// A direct audio URL for a track. Headers are "Name: value" lines for the request;
/// ExpiresAt is unix seconds (googlevideo URLs stop working after that).
type ResolvedStream = {
    Url: string
    Headers: string[]
    ExpiresAt: int64
    Duration: float
}

/// Google rejected the session (missing, expired or signed out).
type AuthExpiredException(message: string) =
    inherit Exception(message)
    
/// A catalog backend (YouTube Music today). Methods run on any thread and fail the
/// task with an exception; callers must not touch UI state from the continuation.
type IMusicProvider =
    inherit IDisposable
    /// Songs matching the query.
    abstract Search: query: string -> Task<IReadOnlyList<Track>>
    /// Metadata for one track id.
    abstract GetTrack: id: string -> Task<Track>
    /// The direct stream of one track id, so the player does not wait for yt-dlp when it starts it.
    abstract Resolve: id: string -> Task<ResolvedStream>
    
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

// browsing

/// What an entry of a browse list points at (tracks are not here: they are Track).
type EntityKind =
    | Album = 0
    | Artist = 1
    | Playlist = 2

/// An album, artist or playlist in a list: a search result, a shelf of Home, a related artist.
/// Id is the browse id (the playlist id for playlists, mixes included).
type Entity = {
    Kind: EntityKind
    Id: string
    Title: string
    Subtitle: string
    ArtUrl: string
}

/// One shelf of Home ("Quick picks", "Mixed for you"...).
type Shelf = {
    Title: string
    Tracks: Track[]
    Entities: Entity[]
}

/// An album or a playlist opened as a page. ArtistId is "" when there is no artist to open.
type TrackCollection = {
    Id: string
    Title: string
    Subtitle: string
    ArtUrl: string
    ArtistId: string
    Tracks: Track[]
}

type ArtistPage = {
    Id: string
    Name: string
    Description: string
    ArtUrl: string
    SongsId: string
    Songs: Track[]
    Albums: Entity[]
    Singles: Entity[]
    Related: Entity[]
}

/// What a typed search found: Tracks for the songs and videos filters, Entities for the rest.
type SearchHits = {
    Tracks: Track[]
    Entities: Entity[]
}

/// The ids behind a track's artist and album, "" when it has none.
type TrackLinks = {
    ArtistId: string
    ArtistName: string
    AlbumId: string
    AlbumName: string
}

/// How many times a track was watched and liked on YouTube; 0 when YouTube does not say.
type TrackStats = {
    Views: int64
    Likes: int64
}

/// Pages of the catalog. Methods run on any thread and fail the task on network trouble.
type IBrowseProvider =
    /// Personalized with a session, generic without one.
    abstract Home: unit -> Task<Shelf[]>
    abstract Album: id: string -> Task<TrackCollection>
    /// A public playlist or a mix.
    abstract Collection: id: string -> Task<TrackCollection>
    abstract Artist: id: string -> Task<ArtistPage>
    /// filter: songs, videos, albums, artists or playlists.
    abstract SearchTyped: query: string * filter: string -> Task<SearchHits>
    abstract Suggestions: query: string -> Task<string[]>
    abstract Links: trackId: string -> Task<TrackLinks>
    /// Views and likes of a track.
    abstract Stats: id: string -> Task<TrackStats>
