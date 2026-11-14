module ClockTests

open System
open Player.Core
open Xunit

[<Fact>]
let ``unixNow is the current UTC time in seconds`` () =
    let before = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    let now = Clock.UnixNow ()
    let after = DateTimeOffset.UtcNow.ToUnixTimeSeconds()

    Assert.InRange(now, before, after)

[<Fact>]
let ``unixNow never goes backwards between two calls`` () =
    let first = Clock.UnixNow ()
    let second = Clock.UnixNow ()

    Assert.True(second >= first)
