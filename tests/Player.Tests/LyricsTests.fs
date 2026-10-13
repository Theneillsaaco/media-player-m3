module LyricsTests

open Player.Core.Lyrics
open Xunit

let private starts (lines: LyricLine[]) = lines |> Array.map (fun l -> l.Start) |> Array.toList
let private texts (lines: LyricLine[]) = lines |> Array.map (fun l -> l.Text) |> Array.toList

[<Fact>]
let ``parses timed lines`` () =
    let lines = Lrc.parse "[00:12.50]Hello\n[01:02.05]World"
    Assert.Equal<float list>([ 12.5; 62.05 ], starts lines)
    Assert.Equal<string list>([ "Hello"; "World" ], texts lines)

[<Fact>]
let ``several marks on one line repeat it in time order`` () =
    let lines = Lrc.parse "[00:10.00][00:30.00]Chorus\n[00:20.00]Verse"
    Assert.Equal<string list>([ "Chorus"; "Verse"; "Chorus" ], texts lines)
    Assert.Equal<float list>([ 10.0; 20.0; 30.0 ], starts lines)

[<Fact>]
let ``metadata tags and untagged lines are ignored`` () =
    let lines = Lrc.parse "[ti:Song]\n[ar:Someone]\nno mark here\n[00:01.00]Only line"
    Assert.Equal<string list>([ "Only line" ], texts lines)

[<Fact>]
let ``offset moves every line sooner and never below zero`` () =
    let lines = Lrc.parse "[offset:+500]\n[00:00.20]A\n[00:10.00]B"
    Assert.Equal<float list>([ 0.0; 9.5 ], starts lines)
    let later = Lrc.parse "[offset:-1000]\n[00:10.00]B"
    Assert.Equal<float list>([ 11.0 ], starts later)

[<Fact>]
let ``word marks of enhanced lrc are dropped`` () =
    let lines = Lrc.parse "[00:05.00]<00:05.00>Hel<00:05.40>lo <00:05.80>you"
    Assert.Equal<string list>([ "Hello you" ], texts lines)

[<Fact>]
let ``fractions of any length and crlf endings parse`` () =
    let lines = Lrc.parse "[00:01.5]A\r\n[00:02.123]B\r\n[00:03]C"
    Assert.Equal<float list>([ 1.5; 2.123; 3.0 ], starts lines)

[<Fact>]
let ``blank timed lines are kept as gaps`` () =
    let lines = Lrc.parse "[00:01.00]A\n[00:05.00]\n[00:09.00]B"
    Assert.Equal<string list>([ "A"; ""; "B" ], texts lines)

[<Fact>]
let ``isSynced tells timed text from plain`` () =
    Assert.True(Lrc.isSynced "[00:01.00]A")
    Assert.False(Lrc.isSynced "Just words\nmore words")
    Assert.False(Lrc.isSynced "")
    Assert.False(Lrc.isSynced null)

[<Fact>]
let ``plain keeps lines, squeezes blank runs and trims the ends`` () =
    let lines = Lrc.plain "One  \nTwo\n\n\n\nThree\n\n"
    Assert.Equal<string list>([ "One"; "Two"; ""; "Three" ], texts lines)
    Assert.True(lines |> Array.forall (fun l -> l.Start = -1.0))

let private sample = Lrc.parse "[00:10.00]A\n[00:20.00]B\n[00:30.00]C"

[<Fact>]
let ``current line is the last one that has started`` () =
    Assert.Equal(-1, LyricsSync.currentIndex sample 0.0)
    Assert.Equal(-1, LyricsSync.currentIndex sample 9.99)
    Assert.Equal(0, LyricsSync.currentIndex sample 10.0)
    Assert.Equal(0, LyricsSync.currentIndex sample 19.99)
    Assert.Equal(1, LyricsSync.currentIndex sample 20.0)
    Assert.Equal(2, LyricsSync.currentIndex sample 999.0)
    Assert.Equal(-1, LyricsSync.currentIndex [||] 5.0)

[<Fact>]
let ``a delay shows lines later`` () =
    Assert.Equal(0, LyricsSync.currentIndex sample (LyricsSync.delayed 10.2 0.0))
    Assert.Equal(-1, LyricsSync.currentIndex sample (LyricsSync.delayed 10.2 0.5))
    Assert.Equal(1, LyricsSync.currentIndex sample (LyricsSync.delayed 19.8 -0.5))

[<Fact>]
let ``long intros count down and short ones do not`` () =
    Assert.Equal(ValueSome 7.0, LyricsSync.introCountdown sample 3.0)
    Assert.Equal(ValueNone, LyricsSync.introCountdown sample 10.0)
    Assert.Equal(ValueNone, LyricsSync.introCountdown (Lrc.parse "[00:03.00]A") 0.0)
    Assert.Equal(ValueSome 8.0, LyricsSync.introCountdown (Lrc.parse "[00:02.00]\n[00:10.00]A") 2.0)

[<Fact>]
let ``nudge moves in half seconds within the limit`` () =
    Assert.Equal(0.5, LyricsSync.nudge 0.0 0.5)
    Assert.Equal(-1.0, LyricsSync.nudge -0.5 -0.5)
    Assert.Equal(0.0, LyricsSync.nudge 0.5 -0.5)
    Assert.Equal(15.0, LyricsSync.nudge 15.0 0.5)
    Assert.Equal(-15.0, LyricsSync.nudge -15.0 -0.5)
