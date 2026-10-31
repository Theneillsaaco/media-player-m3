module SoundTests

open Player.Core.Playback
open Xunit

[<Fact>]
let ``speed steps go up and wrap`` () =
    Assert.Equal(1.25, PlaybackSpeed.next 1.0)
    Assert.Equal(0.5, PlaybackSpeed.next 3.0)
    Assert.Equal(0.75, PlaybackSpeed.next 0.5)

[<Fact>]
let ``speed snaps to the closest step`` () =
    Assert.Equal(1.0, PlaybackSpeed.normalize 1.1)
    Assert.Equal(2.0, PlaybackSpeed.normalize 2.4)
    Assert.Equal(3.0, PlaybackSpeed.normalize 9.0)
    Assert.Equal(1.0, PlaybackSpeed.normalize nan)

[<Fact>]
let ``speed labels`` () =
    Assert.Equal("1×", PlaybackSpeed.label 1.0)
    Assert.Equal("1.25×", PlaybackSpeed.label 1.25)
    Assert.Equal("0.5×", PlaybackSpeed.label 0.5)

[<Fact>]
let ``loud tracks are turned down and quiet ones left alone`` () =
    Assert.Equal(-6.0, TrackLoudness.gainDb 6.0)
    Assert.Equal(0.0, TrackLoudness.gainDb -4.0)
    Assert.Equal(-24.0, TrackLoudness.gainDb 40.0)
    Assert.Equal(0.0, TrackLoudness.gainDb nan)
