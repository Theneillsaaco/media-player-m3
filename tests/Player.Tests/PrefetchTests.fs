module PrefetchTests

open Player.Core.Contracts
open Player.Core.Domain
open Player.Core.Playback
open Xunit

let track (id: string) : Track =
    { Id = id; Title = id; Artist = "a"; Artists = [| "a" |]; Album = ""; DurationSeconds = 100.0; ArtUrl = ""; Kind = Song }

let loaded n start = PlayQueue.replace 1 [ for i in 0 .. n - 1 -> track (string i) ] start PlayQueue.empty

let ids q = Prefetch.upcoming q |> Array.map (fun t -> t.Id) |> Array.toList

[<Fact>]
let ``upcoming gives the next two tracks`` () =
    Assert.Equal<string list>([ "1"; "2" ], ids (loaded 5 0))

[<Fact>]
let ``upcoming stops at the end with repeat off`` () =
    Assert.Equal<string list>([ "2" ], ids (loaded 3 1))
    Assert.Equal<string list>([], ids (loaded 3 2))

[<Fact>]
let ``upcoming wraps with repeat all`` () =
    Assert.Equal<string list>([ "0"; "1" ], ids (loaded 3 2 |> PlayQueue.setRepeat All))

[<Fact>]
let ``upcoming with repeat one is the current track again`` () =
    Assert.Equal<string list>([ "1"; "1" ], ids (loaded 3 1 |> PlayQueue.setRepeat One))

[<Fact>]
let ``upcoming of an empty queue is empty`` () =
    Assert.Equal<string list>([], ids PlayQueue.empty)

[<Fact>]
let ``upcoming follows the shuffled order`` () =
    let q = loaded 10 3 |> PlayQueue.setShuffle 7 true
    let expected = [ q.Items[q.Order[1]].Id; q.Items[q.Order[2]].Id ]
    Assert.Equal<string list>(expected, ids q)

[<Fact>]
let ``a stream is fresh until the margin before it expires`` () =
    let stream: ResolvedStream = { Url = "u"; Headers = [||]; ExpiresAt = 1000L; Duration = 0.0 }
    Assert.True(Prefetch.isFresh 600L stream)
    Assert.False(Prefetch.isFresh 700L stream)
