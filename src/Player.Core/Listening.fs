namespace Player.Core

open System

/// One reading of the player: wall-clock seconds, track position and whether it was paused.
type ListenSample = {
    At: float
    Position: float
    Paused: bool
}

/// Follows one playback of one track. Listen is ValueNone when the track can never be
/// scrobbled (not a song, no artist, a direct link).
type ListenTracker = {
    Listen: Listen voption
    Listened: float
    Last: ListenSample voption
    Reported: bool
}

module Listening =
    /// Playback may move this much further than the wall clock between two samples
    /// (timer jitter) before the jump counts as a seek.
    let private seekTolerance = 0.75
    
    /// The scrobblable form of a track, stamped with when it started.
    let ofTrack (startedAt: int64) (track: Track) : Listen voption =
        let artists = track.Artists |> Option.ofObj |> Option.defaultValue [||]
        
        let artist =
            match artists |> Array.tryFind (String.IsNullOrWhiteSpace >> not) with
            | Some first -> first.Trim()
            | None -> (track.Artist |> Option.ofObj |> Option.defaultValue "").Trim()
            
        let title = (track.Title |> Option.ofObj |> Option.defaultValue "").Trim()
        
        if track.Kind <> Song || startedAt <= 0L || artist = "" || title = "" then
            ValueNone
        else
            ValueSome {
                StartedAt = startedAt
                Artist = artist
                Title = title
                Album = (track.Album |> Option.ofObj |> Option.defaultValue "").Trim()
                DurationSeconds = track.DurationSeconds
            }
            
    let start (startedAt: int64) (track: Track) : ListenTracker =
        { Listen = ofTrack startedAt track
          Listened = 0.0
          Last = ValueNone
          Reported = false }
        
    /// Feeds one reading. Time only counts when both this and the previous reading were
    /// playing and the position advanced no faster than the clock, so pauses and seeks add
    /// nothing. Returns the listen exactly once, the moment the track counts.
    let observe (now: float) (position: float) (paused: bool) (duration: float) (tracker: ListenTracker) =
        let credit =
            match tracker.Last with
            | ValueSome last when not last.Paused && not paused ->
                let moved = position - last.Position
                if moved > 0.0 && moved <= now - last.At + seekTolerance then
                    moved
                else 0.0
            | _ -> 0.0
            
        let listened = tracker.Listened + credit
        
        let next = {
            tracker with
                Listened = listened
                Last = ValueSome { At = now; Position = position; Paused = paused }
        }
        
        match next.Listen with
        | ValueSome listen when not next.Reported && Scrobble.isDue duration listened ->
            struct ({ next with Reported = true }, ValueSome listen)
        | _ -> struct (next, ValueNone)