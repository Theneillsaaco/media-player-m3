module VideoTests

open Player.Core.Contracts
open Player.Core.Playback
open Xunit

let private info id typ counterpartId counterpartType : VideoInfo =
    { TrackId = id; Type = typ; CounterpartId = counterpartId; CounterpartType = counterpartType }

let private omv = "MUSIC_VIDEO_TYPE_OMV"
let private atv = "MUSIC_VIDEO_TYPE_ATV"
let private ugc = "MUSIC_VIDEO_TYPE_UGC"

[<Fact>]
let ``an official music video shows itself by default`` () =
    let plan = Video.plan (info "v1" omv "a1" atv)
    Assert.True(plan.Available)
    Assert.Equal("v1", plan.VideoId)
    Assert.True(plan.Automatic)
    Assert.False(plan.SwapAudio)

[<Fact>]
let ``a user upload is a video on request only`` () =
    let plan = Video.plan (info "u1" ugc "" "")
    Assert.True(plan.Available)
    Assert.False(plan.Automatic)
    Assert.False(plan.SwapAudio)

[<Fact>]
let ``studio audio with a video counterpart swaps to it on request`` () =
    let plan = Video.plan (info "a1" atv "v1" omv)
    Assert.True(plan.Available)
    Assert.Equal("v1", plan.VideoId)
    Assert.False(plan.Automatic)
    Assert.True(plan.SwapAudio)

[<Fact>]
let ``studio audio without a counterpart has no video`` () =
    Assert.False((Video.plan (info "a1" atv "" "")).Available)
    Assert.False((Video.plan (info "a1" atv "x" atv)).Available)

[<Fact>]
let ``unknown types and missing ids have no video`` () =
    Assert.False((Video.plan (info "a1" "" "" "")).Available)
    Assert.False((Video.plan (info "a1" null "" "")).Available)
    Assert.False((Video.plan (info "" omv "" "")).Available)

[<Fact>]
let ``types are matched by their ending, in any case`` () =
    Assert.True(Video.isOfficial "omv")
    Assert.True(Video.isOfficial "MUSIC_VIDEO_TYPE_OMV")
    Assert.True(Video.isStudio "MUSIC_VIDEO_TYPE_ATV")
    Assert.False(Video.isOfficial "MUSIC_VIDEO_TYPE_UGC")

[<Fact>]
let ``the choice wins, otherwise automatic videos follow the setting`` () =
    let official = Video.plan (info "v1" omv "" "")
    let upload = Video.plan (info "u1" ugc "" "")
    Assert.True(Video.showing official true VideoChoice.Unset)
    Assert.False(Video.showing official false VideoChoice.Unset)
    Assert.False(Video.showing official true VideoChoice.Hide)
    Assert.True(Video.showing official false VideoChoice.Show)
    Assert.False(Video.showing upload true VideoChoice.Unset)
    Assert.True(Video.showing upload false VideoChoice.Show)
    Assert.False(Video.showing Video.none true VideoChoice.Show)

[<Fact>]
let ``audio only swaps when the user asked for the video of a studio track`` () =
    let studio = Video.plan (info "a1" atv "v1" omv)
    let official = Video.plan (info "v1" omv "a1" atv)
    Assert.Equal("v1", Video.audioFrom studio VideoChoice.Show)
    Assert.Equal("", Video.audioFrom studio VideoChoice.Unset)
    Assert.Equal("", Video.audioFrom studio VideoChoice.Hide)
    Assert.Equal("", Video.audioFrom official VideoChoice.Show)

[<Fact>]
let ``a stream is fresh until the margin before it expires`` () =
    let stream: VideoStream = { Url = "u"; ExpiresAt = 1000L; Width = 1280; Height = 720 }
    Assert.True(Video.isFresh 600L stream)
    Assert.False(Video.isFresh 700L stream)
