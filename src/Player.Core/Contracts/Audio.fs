namespace Player.Core.Contracts

open System

/// Everything the UI layer needs from an audio backend. No events on purpose: the UI
/// polls on its own thread, so nothing here ever calls back from a foreign thread.
type IAudioEngine =
    inherit IDisposable
    abstract Position: float
    abstract Duration: float
    abstract Paused: bool
    
    /// True when nothing is loaded or playback has finished.
    abstract Idle: bool
    
    /// Starts playing a file path, stream URL or YouTube link, replacing whatever was loaded.
    /// Headers are "Name: value" lines for a direct stream URL; empty otherwise.
    abstract Load: source: string * headers: string[] -> unit
    
    /// Makes this URL the single entry after the current track, so mpv opens it early and goes on
    /// into it without a gap. Replaces any entry queued before. Only call while a track is loaded.
    abstract QueueNext: source: string * headers: string[] -> unit
    /// Drops the entry queued after the current track.
    abstract ClearNext: unit -> unit
    abstract SetPaused: paused: bool -> unit
    abstract Seek: seconds: float -> unit
    abstract SetVolume: percent: int -> unit
    /// True once if a track reached its end since the last call (mpv may have gone on into the queued entry).
    abstract TakeEnded: unit -> bool
    /// Drains pending engine events. Returns the error message if playback failed, otherwise "".
    abstract DrainEvents: unit -> string
    

