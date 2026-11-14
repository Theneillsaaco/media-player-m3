module LyricsExtrasTests

open Player.Core.Lyrics
open Xunit

let private line (start: float) (text: string) : LyricLine = { Start = start; Text = text }

[<Fact>]
let ``extra lines are matched by when they start`` () =
    let main = Lrc.parse "[00:10.00]こんにちは\n[00:20.00]さようなら\n[00:30.00]Hello"
    let roma = Lrc.parse "[00:10.30]konnichiwa\n[00:19.80]sayounara\n[00:30.00]Hello"
    Assert.Equal<string list>([ "konnichiwa"; "sayounara"; "" ], LyricsExtras.pair main roma |> Array.toList)

[<Fact>]
let ``a line with nothing near it gets nothing`` () =
    let main = Lrc.parse "[00:10.00]A\n[01:10.00]B"
    let roma = Lrc.parse "[00:10.00]a-roma"
    Assert.Equal<string list>([ "a-roma"; "" ], LyricsExtras.pair main roma |> Array.toList)

[<Fact>]
let ``the closest candidate wins on either side`` () =
    let main = [| line 10.0 "A" |]
    let before = [| line 9.0 "before"; line 12.5 "after" |]
    let after = [| line 8.0 "before"; line 10.9 "after" |]
    Assert.Equal<string list>([ "before" ], LyricsExtras.pair main before |> Array.toList)
    Assert.Equal<string list>([ "after" ], LyricsExtras.pair main after |> Array.toList)

[<Fact>]
let ``a tie goes to the earlier candidate`` () =
    let main = [| line 10.0 "A" |]
    let extra = [| line 9.0 "early"; line 11.0 "late" |]
    Assert.Equal<string list>([ "early" ], LyricsExtras.pair main extra |> Array.toList)

[<Fact>]
let ``unsorted extra lines still match`` () =
    let main = [| line 10.0 "A"; line 20.0 "B" |]
    let extra = [| line 20.2 "b-roma"; line 10.1 "a-roma" |]
    Assert.Equal<string list>([ "a-roma"; "b-roma" ], LyricsExtras.pair main extra |> Array.toList)

[<Fact>]
let ``blank, untimed and identical lines get nothing`` () =
    let main = [| line 10.0 ""; line -1.0 "plain"; line 20.0 "Same" |]
    let extra = [| line 10.0 "x"; line 20.0 "same" |]
    Assert.Equal<string list>([ ""; ""; "" ], LyricsExtras.pair main extra |> Array.toList)

[<Fact>]
let ``no candidates means no extras`` () =
    let main = [| line 10.0 "A" |]
    Assert.Equal<string list>([ "" ], LyricsExtras.pair main [||] |> Array.toList)
    Assert.Equal<string list>([ "" ], LyricsExtras.pair main [| line 10.0 "" |] |> Array.toList)