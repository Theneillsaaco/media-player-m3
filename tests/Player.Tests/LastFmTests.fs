module LastFmTests

open Xunit
open Player.Scrobble

// Expected values are the MD5 of the concatenation written out by hand, so they pin the rule
// (sorted by name, name+value, secret last, UTF-8), not just the implementation.

[<Fact>]
let ``signature sorts parameters by name and appends the secret`` () =
    let signature =
        LastFmSigning.sign "mysecret" [ "token", "yyyyyyyy"; "method", "auth.getSession"; "api_key", "xxxxxxxx" ]

    Assert.Equal("3e356846fa994eec8e629f135724085b", signature)

[<Fact>]
let ``signature ignores format and callback`` () =
    let plain = LastFmSigning.sign "s" [ "a", "1"; "b", "2" ]
    let withFormat = LastFmSigning.sign "s" [ "format", "json"; "a", "1"; "callback", "x"; "b", "2" ]
    Assert.Equal(plain, withFormat)

[<Fact>]
let ``signature orders batch parameters by name`` () =
    let signature =
        LastFmSigning.sign
            "shh"
            [ "track[1]", "T1"
              "timestamp[1]", "200"
              "method", "track.scrobble"
              "artist[1]", "B"
              "api_key", "KEY"
              "track[0]", "T0"
              "timestamp[0]", "100"
              "artist[0]", "A" ]

    Assert.Equal("69b859c8898c2d9b736b63b39b4418dd", signature)

[<Fact>]
let ``signature hashes non ASCII text as UTF-8`` () =
    let signature =
        LastFmSigning.sign "" [ "api_key", "KEY"; "method", "track.updateNowPlaying"; "artist", "Beyoncé"; "track", "Halo" ]

    Assert.Equal("f19da979633decb60c33bcb3976d2ad3", signature)
