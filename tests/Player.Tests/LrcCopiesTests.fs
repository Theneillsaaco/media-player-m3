module LrcCopiesTests

open Player.Core.Lyrics
open Xunit

let private texts (lines: LyricLine[]) = lines |> Array.map (fun l -> l.Text) |> Array.toList

[<Fact>]
let ``a romanised copy before the original is skipped`` () =
    let lrc = "[00:10.00]Konnichiwa\n[00:50.00]Sayonara\n[00:10.00]こんにちは\n[00:50.00]さようなら"
    Assert.Equal<string list>([ "こんにちは"; "さようなら" ], texts (Lrc.parse lrc))

[<Fact>]
let ``with no difference between copies the first one stays`` () =
    let lrc = "[00:10.00]One\n[00:50.00]Two\n[00:10.00]Uno\n[00:50.00]Dos"
    Assert.Equal<string list>([ "One"; "Two" ], texts (Lrc.parse lrc))

[<Fact>]
let ``a chorus mark far ahead does not split the song`` () =
    let lrc = "[00:10.00][01:30.00]Chorus\n[00:20.00]Verse\n[00:40.00]Bridge"
    Assert.Equal<string list>([ "Chorus"; "Verse"; "Bridge"; "Chorus" ], texts (Lrc.parse lrc))

[<Fact>]
let ``titles are shortened for the lyrics search`` () =
    Assert.Equal("Song", LyricTitle.shorten "Song (feat. Someone) - Remastered 2011")
    Assert.Equal("Song", LyricTitle.shorten "Song - 日本語訳")
    Assert.Equal("Song", LyricTitle.shorten "Song [ft. Someone]")
    Assert.Equal("Plain", LyricTitle.shorten "Plain")
    Assert.Equal("", LyricTitle.shorten null)
