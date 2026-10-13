namespace Player.Core.Lyrics

open System

/// Which line is current and when to count down, from the playback position.
module LyricsSync =
    /// An intro shorter than this is not worth a countdown.
    let introThreshold = 5.0
    
    /// The biggest delay the user can set, either way, in seconds.
    let maxDelay = 15.0

    /// Index of the last line that has started by this position (binary search), or -1 before the first.
    let currentIndex (lines: LyricLine[]) (position: float) =
        let mutable low = 0
        let mutable high = lines.Length - 1
        let mutable found = -1
        
        while low <= high do
            let mid = (low + high) / 2
            
            if lines[mid].Start <= position then
                found <- mid
                low <- mid + 1
            else
                high <- mid - 1
                
        found
        
    /// The playback position the lyrics are looked up at. A positive delay shows every line later.
    let delayed (position: float) (delay: float) = position - delay

    /// Seconds left until the first sung line, while the song is still in a long intro.
    let introCountdown (lines: LyricLine[]) (position: float) : float voption =
        match lines |> Array.tryFind (fun l -> l.Text <> "") with
        | Some first when first.Start >= introThreshold && position < first.Start -> ValueSome(first.Start - position)
        | _ -> ValueNone
        
    /// The delay after a nudge: steps of half a second, within +-maxDelay.
    let nudge (current: float) (delta: float) =
        let stepped = Math.Round((current + delta) * 2.0, MidpointRounding.AwayFromZero) / 2.0
        Math.Max(-maxDelay, min maxDelay stepped)
    