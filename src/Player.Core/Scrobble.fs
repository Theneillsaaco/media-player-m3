namespace Player.Core

/// Last.fm / ListenBrainz rule: a track counts once it has played for half its length or
/// four minutes, whichever comes first, and tracks under thirty seconds never count.
module Scrobble =
    let minTrackSeconds = 30.0
    let maxWaitSeconds = 240.0

    /// Seconds of listening after which the track counts, or None if it is too short.
    let threshold (durationSeconds: float) =
        if durationSeconds < minTrackSeconds then None
        else Some (min (durationSeconds / 2.0) maxWaitSeconds)

    let isDue (durationSeconds: float) (listenedSeconds: float) =
        match threshold durationSeconds with
        | Some t -> listenedSeconds >= t
        | None -> false
