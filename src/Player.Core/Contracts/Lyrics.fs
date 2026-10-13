namespace Player.Core.Contracts

open System.Threading.Tasks
open Player.Core.Domain

/// What a lyrics source found for a track. Text is LRC (with [mm:ss.xx] marks) when Synced, plain lines otherwise.
type LyricsResult = {
    Found: bool
    /// The source knows the track has no lyrics because it is instrumental.
    Instrumental: bool
    Synced: bool
    Text: string
    Source: string
}

/// Somewhere lyrics can come from (YouTube Music, LRCLIB). Methods run on any thread. A track the source does
/// not have is a result with Found = false; network trouble fails the task, so the caller can tell "no lyrics"
/// from "could not ask".
type ILyricsSource =
    abstract Name: string
    abstract Find: track: Track -> Task<LyricsResult>