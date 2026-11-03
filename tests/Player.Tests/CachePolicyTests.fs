module CachePolicyTests

open Player.Core.Playback
open Xunit

let private files (used: (string * int64) list) = used |> List.map (fun (id, at) -> struct (id, at))

[<Fact>]
let ``nothing goes while the cache is within its limit`` () =
    Assert.Empty(CachePolicy.evict 3 [] (files [ "a", 1L; "b", 2L; "c", 3L ]))

[<Fact>]
let ``the least recently used go first`` () =
    let gone = CachePolicy.evict 2 [] (files [ "a", 30L; "b", 10L; "c", 20L; "d", 40L ]) |> Array.toList
    Assert.Equal<string list>([ "b"; "c" ], gone)

[<Fact>]
let ``kept songs are never evicted, even if that leaves the cache over its limit`` () =
    let gone = CachePolicy.evict 1 [ "b"; "c" ] (files [ "a", 30L; "b", 10L; "c", 20L ]) |> Array.toList
    Assert.Equal<string list>([ "a" ], gone)

[<Fact>]
let ``a limit of zero empties the cache but for what is kept`` () =
    let gone = CachePolicy.evict 0 [ "a" ] (files [ "a", 1L; "b", 2L ]) |> Array.toList
    Assert.Equal<string list>([ "b" ], gone)

[<Fact>]
let ``limits are normalized`` () =
    Assert.Equal(30, CachePolicy.normalizeLimit -5)
    Assert.Equal(0, CachePolicy.normalizeLimit 0)
    Assert.Equal(100, CachePolicy.normalizeLimit 100)
    Assert.Equal(1000, CachePolicy.normalizeLimit 99999)
