module ScrobbleTests

open Player.Core.Scrobbling
open Xunit

[<Fact>]
let ``tracks under 30 seconds never count`` () =
    Assert.Equal(None, Scrobble.threshold 29.9)
    Assert.False(Scrobble.isDue 29.0 29.0)

[<Fact>]
let ``threshold is half the track`` () =
    Assert.Equal(Some 90.0, Scrobble.threshold 180.0)

[<Fact>]
let ``threshold caps at four minutes`` () =
    Assert.Equal(Some 240.0, Scrobble.threshold 1200.0)

[<Fact>]
let ``isDue flips at the threshold`` () =
    Assert.False(Scrobble.isDue 180.0 89.9)
    Assert.True(Scrobble.isDue 180.0 90.0)
