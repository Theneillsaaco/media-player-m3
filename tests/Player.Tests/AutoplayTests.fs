module AutoplayTests

open Player.Core.Domain
open Player.Core.Playback
open Xunit

let private track (id: string) (kind: TrackKind) : Track =
    { Id = id; Title = id; Artist = "a"; Artists = [| "a" |]; Album = ""; DurationSeconds = 100.0; ArtUrl = ""; Kind = kind }

let private loaded n start =
    PlayQueue.replace 1 [ for i in 0 .. n - 1 -> track (string i) Song ] start PlayQueue.empty

let private song: Track =
    { Id = "a"; Title = "t"; Artist = "x"; Artists = [| "x" |]; Album = ""; DurationSeconds = 200.0; ArtUrl = ""; Kind = Song }

[<Fact>]
let ``autoplay is wanted on the last track with repeat off`` () =
    Assert.True(Autoplay.wanted (loaded 3 2))
    Assert.False(Autoplay.wanted (loaded 3 1))

[<Fact>]
let ``autoplay is not wanted when the queue repeats or is empty`` () =
    Assert.False(Autoplay.wanted (loaded 3 2 |> PlayQueue.setRepeat All))
    Assert.False(Autoplay.wanted (loaded 3 2 |> PlayQueue.setRepeat One))
    Assert.False(Autoplay.wanted PlayQueue.empty)

[<Fact>]
let ``fresh keeps songs that are new and drops the rest`` () =
    let found = [ track "a" Song; track "b" Song; track "b" Song; track "c" Video; track "" Song; track "d" Song ]
    let ids = Autoplay.fresh [ "a" ] found |> Array.map (fun t -> t.Id) |> Array.toList
    Assert.Equal<string list>([ "b"; "d" ], ids)

[<Fact>]
let ``a listen can be marked as not chosen`` () =
    let tracker = Listening.start 1000L song
    Assert.True(tracker.Listen.Value.Chosen)
    Assert.False((Listening.unchosen tracker).Listen.Value.Chosen)
