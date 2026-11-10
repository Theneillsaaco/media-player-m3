module LyricsExtrasTests

open Player.Core.Lyrics
open Xunit

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
    