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
    