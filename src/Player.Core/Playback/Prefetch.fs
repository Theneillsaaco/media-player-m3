namespace Player.Core.Playback

open Player.Core.Contracts
open Player.Core.Domain

/// Which tracks are worth resolving ahead of time, and whether a resolved URL can still be used.
module Prefetch =
    /// How many tracks ahead are resolved.
    let depth = 2
    
    /// A URL is dropped this many seconds before the time it says it expires.
    let margin = 300L
    
    let isFresh (now: int64) (stream: ResolvedStream) =
        stream.ExpiresAt - margin > now
        
    /// The tracks that will play after the current one, in the order `PlayQueue.advance` gives them
    /// (so shuffle, repeat all and repeat one are respected). Up to `depth`; may repeat a track.
    let upcoming (q: QueueState) : Track[] =
        let rec go (state: QueueState) n acc =
            if n = 0 then
                List.rev acc
            else
                match PlayQueue.advance state with
                | None -> List.rev acc
                | Some following ->
                    match PlayQueue.current following with
                    | None -> List.rev acc
                    | Some track -> go following (n - 1) (track :: acc)
        
        go q depth [] |> List.toArray