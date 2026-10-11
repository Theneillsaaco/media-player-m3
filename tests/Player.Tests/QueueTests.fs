module QueueTests

open Player.Core.Domain
open Player.Core.Playback
open Xunit

let track (id: string) =
    { Id = id; Title = id; Artist = "a"; Artists = [| "a" |]; Album = ""; DurationSeconds = 100.0; ArtUrl = ""; Kind = Song }

let tracks n = [ for i in 0 .. n - 1 -> track (string i) ]

let idAt q = PlayQueue.current q |> Option.map (fun t -> t.Id)

let idOf (q: QueueState option) = q |> Option.bind PlayQueue.current |> Option.map (fun t -> t.Id)

let loaded n start = PlayQueue.replace 1 (tracks n) start PlayQueue.empty

[<Fact>]
let ``replace starts at the chosen item`` () =
    Assert.Equal(Some "2", idAt (loaded 5 2))

[<Fact>]
let ``replace clamps the start index`` () =
    Assert.Equal(Some "4", idAt (loaded 5 99))
    Assert.Equal(Some "0", idAt (loaded 5 -3))

[<Fact>]
let ``replace with no tracks gives an empty queue and keeps settings`` () =
    let q = PlayQueue.empty |> PlayQueue.cycleRepeat |> PlayQueue.replace 1 [] 0
    Assert.Equal(None, idAt q)
    Assert.Equal(All, q.Repeat)

[<Fact>]
let ``next and previous walk the queue`` () =
    let q = loaded 3 0
    Assert.Equal(Some "1", idAt (PlayQueue.next q))
    Assert.Equal(Some "0", idAt (PlayQueue.previous (PlayQueue.next q)))

[<Fact>]
let ``next stops at the end with repeat off`` () =
    let q = loaded 3 2
    Assert.False(PlayQueue.hasNext q)
    Assert.Equal(Some "2", idAt (PlayQueue.next q))

[<Fact>]
let ``previous stays at the start with repeat off`` () =
    Assert.Equal(Some "0", idAt (PlayQueue.previous (loaded 3 0)))

[<Fact>]
let ``repeat all wraps both ways`` () =
    let q = loaded 3 2 |> PlayQueue.setRepeat All
    Assert.True(PlayQueue.hasNext q)
    Assert.Equal(Some "0", idAt (PlayQueue.next q))
    Assert.Equal(Some "2", idAt (PlayQueue.previous (loaded 3 0 |> PlayQueue.setRepeat All)))

[<Fact>]
let ``advance moves on, repeat one stays, end gives None`` () =
    let q = loaded 2 0
    Assert.Equal(Some "1", idOf (PlayQueue.advance q))
    Assert.Equal(Some "0", idOf (PlayQueue.advance (PlayQueue.setRepeat One q)))
    Assert.True((PlayQueue.advance (loaded 2 1)).IsNone)
    Assert.Equal(Some "0", idOf (PlayQueue.advance (PlayQueue.setRepeat All (loaded 2 1))))

[<Fact>]
let ``advance on an empty queue is None`` () =
    Assert.True((PlayQueue.advance PlayQueue.empty).IsNone)

[<Fact>]
let ``manual next with repeat one still moves on`` () =
    Assert.Equal(Some "1", idAt (PlayQueue.next (PlayQueue.setRepeat One (loaded 3 0))))

[<Fact>]
let ``cycleRepeat goes off, all, one, off`` () =
    let q1 = PlayQueue.cycleRepeat PlayQueue.empty
    let q2 = PlayQueue.cycleRepeat q1
    let q3 = PlayQueue.cycleRepeat q2
    let names = [ PlayQueue.empty; q1; q2; q3 ] |> List.map PlayQueue.repeatName
    Assert.Equal<string list>([ "off"; "all"; "one"; "off" ], names)

[<Fact>]
let ``add appends without moving the current track`` () =
    let q = PlayQueue.add [ track "x" ] (loaded 3 1)
    Assert.Equal(4, PlayQueue.count q)
    Assert.Equal(Some "1", idAt q)
    Assert.Equal("x", (PlayQueue.ordered q |> Array.last).Id)

[<Fact>]
let ``add to an empty queue leaves nothing current`` () =
    let q = PlayQueue.add [ track "x" ] PlayQueue.empty
    Assert.Equal(1, PlayQueue.count q)
    Assert.Equal(None, idAt q)
    Assert.Equal(Some "x", idAt (PlayQueue.jumpTo 0 q))

[<Fact>]
let ``jumpTo ignores out of range positions`` () =
    let q = loaded 3 1
    Assert.Equal(Some "1", idAt (PlayQueue.jumpTo 7 q))
    Assert.Equal(Some "2", idAt (PlayQueue.jumpTo 2 q))

[<Fact>]
let ``shuffle keeps the current track first and is a permutation`` () =
    let q = PlayQueue.setShuffle 42 true (loaded 20 7)
    Assert.Equal(Some "7", idAt q)
    Assert.Equal(0, q.Position)
    Assert.Equal<int list>([ 0 .. 19 ], q.Order |> Array.toList |> List.sort)

[<Fact>]
let ``shuffle is deterministic for a seed`` () =
    let a = PlayQueue.setShuffle 5 true (loaded 20 0)
    let b = PlayQueue.setShuffle 5 true (loaded 20 0)
    Assert.Equal<int list>(Array.toList a.Order, Array.toList b.Order)

[<Fact>]
let ``unshuffle restores order and keeps the current track`` () =
    let shuffled = loaded 10 4 |> PlayQueue.setShuffle 9 true |> PlayQueue.next
    let q = PlayQueue.setShuffle 0 false shuffled
    Assert.Equal(idAt shuffled, idAt q)
    Assert.Equal<int list>([ 0 .. 9 ], Array.toList q.Order)

[<Fact>]
let ``replace while shuffled starts at the chosen item`` () =
    let q = PlayQueue.setShuffle 3 true PlayQueue.empty |> PlayQueue.replace 8 (tracks 10) 6
    Assert.Equal(Some "6", idAt q)
    Assert.True(q.Shuffle)
