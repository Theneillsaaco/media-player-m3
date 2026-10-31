module PlayErrorsTests

open Player.Core.Playback
open Xunit

[<Fact>]
let ``known errors map to a text key`` () =
    Assert.Equal("error.region", PlayErrors.reason "ERROR: The uploader has not made this video available in your country")
    Assert.Equal("error.region", PlayErrors.reason "Video blocked in your country")
    Assert.Equal("error.age", PlayErrors.reason "Sign in to confirm your age")
    Assert.Equal("error.signIn", PlayErrors.reason "Sign in to confirm you are not a bot")
    Assert.Equal("error.private", PlayErrors.reason "ERROR: Private video. Sign in if you've been granted access")
    Assert.Equal("error.removed", PlayErrors.reason "Video unavailable")
    Assert.Equal("error.noFormat", PlayErrors.reason "Requested format is not available")

[<Fact>]
let ``a 403 wins over the generic download failure`` () =
    Assert.Equal("error.stopped", PlayErrors.reason "unable to download video data: HTTP Error 403: Forbidden")
    Assert.Equal("error.network", PlayErrors.reason "Unable to download webpage: <urlopen error timed out>")
    Assert.Equal("error.network", PlayErrors.reason "dial tcp: lookup music.youtube.com: no such host")

[<Fact>]
let ``unknown or empty errors give no key`` () =
    Assert.Equal("", PlayErrors.reason "something nobody has seen")
    Assert.Equal("", PlayErrors.reason "")
    Assert.Equal("", PlayErrors.reason null)

[<Fact>]
let ``firstLine keeps only the first line`` () =
    Assert.Equal("boom", PlayErrors.firstLine "  boom  \nat line 2\nat line 3")
    Assert.Equal("", PlayErrors.firstLine null)
