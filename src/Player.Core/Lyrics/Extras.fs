namespace Player.Core.Lyrics

open System

/// Extra lines (the romaji of a song) matched to the lines of the lyrics by when they start.
module LyricsExtras =
    /// How far apart two lines may start and still be the same line.
    let tolerance = 2.0
    
    /// For every line, the text of the extra line that starts closest to it; "" when there is none within the
    /// tolerance or when it is the same text (a line that is already in latin letters).
    let pair (lines: LyricLine[]) (extra: LyricLine[]) : string[] =
        let candidates = extra |> Array.filter (fun e -> e.Text <> "")
        
        lines
        |> Array.map (fun l ->
            if l.Text = "" || l.Start < 0.0 || candidates.Length = 0 then
                ""
            else
                let nearest = candidates |> Array.minBy (fun e -> abs (e.Start - l.Start))
                
                if abs (nearest.Start - l.Start) <= tolerance
                    && not (String.Equals(nearest.Text, l.Text, StringComparison.OrdinalIgnoreCase)) then
                    nearest.Text
                else
                    "")
        