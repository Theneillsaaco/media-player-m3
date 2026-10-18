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

let order q = PlayQueue.ordered q |> Array.map (fun t -> t.Id) |> Array.toList

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

[<Fact>]
let ``addNext inserts right after the current track`` () =
    let q = PlayQueue.addNext [ track "x" ] (loaded 4 1)
    Assert.Equal<string list>([ "0"; "1"; "x"; "2"; "3" ], order q)
    Assert.Equal(Some "1", idAt q)
    Assert.Equal(Some "x", idAt (PlayQueue.next q))

[<Fact>]
let ``addNext while shuffled inserts after the current one in play order`` () =
    let q = loaded 8 3 |> PlayQueue.setShuffle 11 true
    let after = PlayQueue.addNext [ track "x" ] q
    Assert.Equal(Some "3", idAt after)
    Assert.Equal(Some "x", idAt (PlayQueue.next after))
    Assert.Equal(9, PlayQueue.count after)
    Assert.Equal<int list>([ 0 .. 8 ], after.Order |> Array.toList |> List.sort)

[<Fact>]
let ``addNext to an empty queue leaves nothing current`` () =
    let q = PlayQueue.addNext [ track "x" ] PlayQueue.empty
    Assert.Equal(1, PlayQueue.count q)
    Assert.Equal(None, idAt q)

[<Fact>]
let ``move keeps the current track current`` () =
    let q = loaded 5 2
    Assert.Equal<string list>([ "0"; "2"; "1"; "3"; "4" ], order (PlayQueue.move 2 1 q))
    Assert.Equal(Some "2", idAt (PlayQueue.move 2 1 q))
    Assert.Equal(Some "2", idAt (PlayQueue.move 0 4 q))
    Assert.Equal(Some "2", idAt (PlayQueue.move 4 0 q))
    Assert.Equal(Some "2", idAt (PlayQueue.move 1 3 q))

[<Fact>]
let ``move puts the entry at the target position`` () =
    let q = loaded 5 0
    Assert.Equal<string list>([ "1"; "2"; "3"; "0"; "4" ], order (PlayQueue.move 0 3 q))
    Assert.Equal<string list>([ "4"; "0"; "1"; "2"; "3" ], order (PlayQueue.move 4 0 q))

[<Fact>]
let ``move ignores bad positions`` () =
    let q = loaded 3 1
    Assert.Equal<string list>([ "0"; "1"; "2" ], order (PlayQueue.move -1 2 q))
    Assert.Equal<string list>([ "0"; "1"; "2" ], order (PlayQueue.move 0 9 q))
    Assert.Equal<string list>([ "0"; "1"; "2" ], order (PlayQueue.move 1 1 q))

[<Fact>]
let ``move while shuffled changes the play order only`` () =
    let q = loaded 6 2 |> PlayQueue.setShuffle 4 true
    let before = order q
    let after = PlayQueue.move 0 3 q
    Assert.Equal(idAt q, idAt after)
    Assert.Equal<string list>(List.sort before, List.sort (order after))
    Assert.Equal(before[1], (order after)[0])

[<Fact>]
let ``a moved queue survives turning shuffle on and off`` () =
    let q = PlayQueue.move 0 2 (loaded 4 3)
    let again = q |> PlayQueue.setShuffle 1 true |> PlayQueue.setShuffle 0 false
    Assert.Equal<string list>(order q, order again)
    Assert.Equal(idAt q, idAt again)

[<Fact>]
let ``remove before the current track keeps it current`` () =
    let q = PlayQueue.remove 0 (loaded 4 2)
    Assert.Equal<string list>([ "1"; "2"; "3" ], order q)
    Assert.Equal(Some "2", idAt q)

[<Fact>]
let ``remove after the current track keeps it current`` () =
    let q = PlayQueue.remove 3 (loaded 4 1)
    Assert.Equal<string list>([ "0"; "1"; "2" ], order q)
    Assert.Equal(Some "1", idAt q)

[<Fact>]
let ``removing the current track makes the next one current`` () =
    let q = PlayQueue.remove 1 (loaded 4 1)
    Assert.Equal<string list>([ "0"; "2"; "3" ], order q)
    Assert.Equal(Some "2", idAt q)

[<Fact>]
let ``removing the last current track falls back to the new last one`` () =
    let q = PlayQueue.remove 2 (loaded 3 2)
    Assert.Equal<string list>([ "0"; "1" ], order q)
    Assert.Equal(Some "1", idAt q)

[<Fact>]
let ``removing the only track empties the queue`` () =
    let q = PlayQueue.remove 0 (loaded 1 0)
    Assert.Equal(0, PlayQueue.count q)
    Assert.Equal(None, idAt q)

[<Fact>]
let ``remove while shuffled keeps a valid permutation`` () =
    let q = loaded 8 5 |> PlayQueue.setShuffle 6 true
    let after = PlayQueue.remove 3 q
    Assert.Equal(7, PlayQueue.count after)
    Assert.Equal<int list>([ 0 .. 6 ], after.Order |> Array.toList |> List.sort)
    Assert.Equal(Some "5", idAt after)

[<Fact>]
let ``remove ignores bad positions`` () =
    Assert.Equal<string list>([ "0"; "1" ], order (PlayQueue.remove 5 (loaded 2 0)))
    Assert.Equal<string list>([ "0"; "1" ], order (PlayQueue.remove -1 (loaded 2 0)))

[<Fact>]
let ``clear drops the tracks and keeps repeat and shuffle`` () =
    let q = loaded 3 1 |> PlayQueue.setRepeat All |> PlayQueue.setShuffle 2 true |> PlayQueue.clear
    Assert.Equal(0, PlayQueue.count q)
    Assert.Equal(All, q.Repeat)
    Assert.True(q.Shuffle)
