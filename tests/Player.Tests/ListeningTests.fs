module ListeningTests

open Player.Core.Domain
open Player.Core.Playback
open Xunit

let song: Track =
    { Id = "a"; Title = "t"; Artist = "x, y"; Artists = [| "x"; "y" |]; Album = " alb "
      DurationSeconds = 200.0; ArtUrl = ""; Kind = Song }

/// Feeds (wall seconds, position, paused) readings to a tracker for a 200 s track.
/// Returns the tracker and how many times it reported the listen.
let run (samples: (float * float * bool) list) tracker =
    samples
    |> List.fold
        (fun (t, fired) (now, position, paused) ->
            let struct (next, due) = Listening.observe now position paused 200.0 t
            next, fired + (if due.IsSome then 1 else 0))
        (tracker, 0)

/// n readings 0.25 s apart, playing from position p0 at wall time t0 (the first one included).
let playing t0 p0 n = [ for i in 0 .. n -> (t0 + 0.25 * float i, p0 + 0.25 * float i, false) ]

let fresh = Listening.start 1000L song

[<Fact>]
let ``uses the first artist and trims fields`` () =
    let listen = (Listening.ofTrack 5L song).Value
    Assert.Equal("x", listen.Artist)
    Assert.Equal("alb", listen.Album)
    Assert.Equal(5L, listen.StartedAt)

[<Fact>]
let ``falls back to the joined artist when the list is empty`` () =
    let listen = (Listening.ofTrack 5L { song with Artists = [||]; Artist = "solo" }).Value
    Assert.Equal("solo", listen.Artist)

[<Fact>]
let ``videos, direct links and tracks without artist are not listens`` () =
    Assert.True((Listening.ofTrack 5L { song with Kind = Video }).IsNone)
    Assert.True((Listening.ofTrack 5L { song with Artists = [||]; Artist = "" }).IsNone)
    Assert.True((Listening.ofTrack 0L song).IsNone)

[<Fact>]
let ``continuous playback reports exactly once at half the track`` () =
    let t, fired = run (playing 0.0 0.0 440) fresh
    Assert.Equal(1, fired)
    Assert.True(t.Reported)

[<Fact>]
let ``not reported before the threshold`` () =
    let _, fired = run (playing 0.0 0.0 380) fresh // 95 s of a 200 s track
    Assert.Equal(0, fired)

[<Fact>]
let ``paused time does not count`` () =
    let samples =
        playing 0.0 0.0 240 // 60 s
        @ [ (1060.0, 60.0, true) ] // paused for a long while
        @ [ for i in 1..40 -> (1060.0 + 0.25 * float i, 60.0 + 0.25 * float i, false) ] // 10 s more
    let t, fired = run samples fresh
    Assert.Equal(0, fired)
    Assert.Equal(69.75, t.Listened, 2) // the first reading after the pause only re-anchors

[<Fact>]
let ``a seek forward does not count`` () =
    let samples =
        playing 0.0 0.0 120 // 30 s
        @ [ for j in 0..40 -> (30.25 + 0.25 * float j, 150.0 + 0.25 * float j, false) ] // jump to 150 s, then 10 s
    let t, fired = run samples fresh
    Assert.Equal(0, fired)
    Assert.Equal(40.0, t.Listened, 2)

[<Fact>]
let ``a seek back does not count`` () =
    let samples = playing 0.0 100.0 40 @ [ (10.25, 5.0, false); (10.5, 5.25, false) ]
    let t, _ = run samples fresh
    Assert.Equal(10.25, t.Listened, 2) // 10 s before the jump, 0.25 s after it

[<Fact>]
let ``short tracks never report`` () =
    let short = Listening.start 1000L { song with DurationSeconds = 20.0 }
    let mutable t = short
    let mutable fired = 0
    for (now, pos, paused) in playing 0.0 0.0 80 do
        let struct (next, due) = Listening.observe now pos paused 20.0 t
        t <- next
        if due.IsSome then fired <- fired + 1
    Assert.Equal(0, fired)

[<Fact>]
let ``an ineligible track never reports`` () =
    let video = Listening.start 1000L { song with Kind = Video }
    let _, fired = run (playing 0.0 0.0 440) video
    Assert.Equal(0, fired)
