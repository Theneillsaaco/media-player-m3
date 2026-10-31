namespace Player.Core.Lyrics

open System
open System.Globalization
open System.Text.RegularExpressions

/// One line of lyrics. Start is in seconds; it is -1 for lines of plain (untimed) lyrics.
type LyricLine = {
    Start: float
    Text: string
}

/// The LRC format: "[mm:ss.xx]text", several marks per line, "[offset:ms]", metadata tags like "[ar:...]" and
/// the optional per-word marks "<mm:ss.xx>" of enhanced LRC (those are dropped, the line keeps its own mark).
module Lrc =
    let private tags = Regex(@"^\s*((?:\[[^\]]*\]\s*)+)", RegexOptions.Compiled)
    let private time = Regex(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled)
    let private offsetTag = Regex(@"\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.Compiled ||| RegexOptions.IgnoreCase)
    let private wordMark = Regex(@"<\d{1,3}:\d{1,2}(?:[.:]\d{1,3})?>", RegexOptions.Compiled)

    /// How far back the first mark of a line must jump, from the first mark of the line before it, to count as
    /// the start of another copy of the song.
    let private restart = 30.0

    let private seconds (m: Match) =
        let whole = float (int m.Groups[1].Value * 60 + int m.Groups[2].Value)

        if m.Groups[3].Success then
            whole + Double.Parse("0." + m.Groups[3].Value, CultureInfo.InvariantCulture)
        else
            whole

    let private splitLines (text: string) =
        (if isNull text then "" else text).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')

    /// How much of a copy is outside ASCII: the original of a song in Japanese or Korean, not its romanisation.
    let private nonAscii (copy: LyricLine list) =
        copy |> List.sumBy (fun l -> l.Text |> Seq.filter (fun c -> int c > 127) |> Seq.length)

    /// The timed lines of an LRC text, in time order (lines that share a mark keep their written order).
    /// "[offset:+500]" makes everything appear 500 ms sooner, as most players read it.
    /// Some files hold the song more than once, as a romanised or translated copy after the original, and the
    /// second copy starts again from the top. Such a file is split where its time jumps back, and the copy with
    /// the most non-ASCII text is kept; with no difference between them, the first.
    let parse (text: string) : LyricLine[] =
        let mutable offset = 0.0
        let mutable previous = -1.0
        let entries = ResizeArray<LyricLine>()
        let breaks = ResizeArray<int>() // where in entries another copy of the song starts

        for line in splitLines text do
            let lead = tags.Match line

            if lead.Success then
                let marks = lead.Groups[1].Value
                let found = offsetTag.Match marks

                if found.Success then
                    offset <- float (int found.Groups[1].Value) / 1000.0
                else
                    let body = wordMark.Replace(line.Substring lead.Length, "").Trim()
                    let stamps = time.Matches marks

                    if stamps.Count > 0 then
                        let first = seconds stamps[0]

                        if previous >= 0.0 && first < previous - restart then
                            breaks.Add entries.Count

                        previous <- first

                    for m in stamps do
                        entries.Add { Start = seconds m; Text = body }

        let edges = List.ofSeq breaks
        let starts = 0 :: edges
        let ends = edges @ [ entries.Count ]
        let copies = List.map2 (fun a b -> [ for i in a .. b - 1 -> entries[i] ]) starts ends
        let chosen = copies |> List.reduce (fun best copy -> if nonAscii copy > nonAscii best then copy else best)

        chosen
        |> List.map (fun l -> { l with Start = max 0.0 (l.Start - offset) })
        |> List.sortBy (fun l -> l.Start)
        |> List.toArray

    /// True when the text has at least one timed line.
    let isSynced (text: string) =
        (parse text).Length > 0

    /// Untimed lyrics as lines (Start = -1): trailing spaces dropped, long runs of blank lines squeezed to one.
    let plain (text: string) : LyricLine[] =
        let lines = splitLines text |> Array.map (fun l -> l.Trim())
        let result = ResizeArray<LyricLine>()
        let mutable blanks = 0

        for line in lines do
            if line = "" then
                blanks <- blanks + 1
                if blanks = 1 && result.Count > 0 then result.Add { Start = -1.0; Text = "" }
            else
                blanks <- 0
                result.Add { Start = -1.0; Text = line }

        while result.Count > 0 && result.[result.Count - 1].Text = "" do
            result.RemoveAt(result.Count - 1)

        result.ToArray()


    /// The lines to show for a stored text: timed when it is synced, plain otherwise.
    let lines (synced: bool) (text: string) =
        if synced then parse text else plain text
