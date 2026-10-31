namespace Player.Core.Playback

open System.Collections.Generic
open Player.Core.Domain

/// When the queue runs out, what to add so the music goes on.
module Autoplay =
    /// The queue has played out: a track is current, nothing follows it and nothing repeats.
    let wanted (q: QueueState) : bool =
        q.Repeat = Off && (PlayQueue.current q).IsSome && not (PlayQueue.hasNext q)

    /// The recommended tracks worth adding: songs only, none already known (the queue) and none twice.
    let fresh (known: string seq) (found: Track seq) : Track[] =
        let seen = HashSet<string>(known)

        found
        |> Seq.filter (fun t -> t.Kind = Song && t.Id <> "" && seen.Add(t.Id))
        |> Seq.toArray
