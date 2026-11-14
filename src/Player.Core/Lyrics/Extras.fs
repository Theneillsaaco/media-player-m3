namespace Player.Core.Lyrics

open System

/// Extra lines (the romaji of a song) matched to the lines of the lyrics by when they start.
module LyricsExtras =
    /// How far apart two lines may start and still be the same line.
    let tolerance = 2.0
    
    /// For every line, the text of the extra line that starts closest to it; "" when there is none within the
    /// tolerance or when it is the same text (a line that is already in latin letters).
    /// The candidates are sorted once and each line finds its neighbours by binary search: O((n + m) log m)
    /// instead of scanning every candidate for every line. On a tie the earlier candidate wins.    let pair (lines: LyricLine[]) (extra: LyricLine[]) : string[] =
    let pair (lines: LyricLine[]) (extra: LyricLine[]) : string[] =
        let candidates =
            extra
            |> Seq.filter (fun e -> e.Text <> "")
            |> Seq.sortBy _.Start // stable
            |> Seq.toArray
        
        let firstFrom (start: float) =
            let mutable low = 0
            let mutable high = candidates.Length
            
            while low < high do
                let mid = (low + high) / 2
                
                if candidates[mid].Start < start then
                    low <- mid + 1
                else
                    high <- mid
                    
            low
        
        let nearest (start: float) =
            let i = firstFrom start
            
            if i = 0 then
                candidates[0]
            elif i = candidates.Length then
                candidates[i - 1]
            else
                let before = candidates[i - 1]
                let after = candidates[i]
                
                if abs (start - before.Start) <= abs (after.Start - start) then
                    before
                else after
            
        lines
        |> Array.map (fun l ->
            if l.Text = "" || l.Start < 0.0 || candidates.Length = 0 then
                ""
            else
                let found = nearest l.Start
                
                if abs (found.Start - l.Start) <= tolerance
                    && not (String.Equals(found.Text, l.Text, StringComparison.OrdinalIgnoreCase)) then
                    found.Text
                else
                    "")
        