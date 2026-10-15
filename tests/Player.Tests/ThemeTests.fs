module ThemeTests

open System
open System.IO
open System.Text.Json
open Player.Core.Theme
open Xunit

// Sung's m3color.cpp (MIT) compiled against Qt is the oracle: Data/m3color-oracle.json holds what it produced for
// 8 seeds x 5 variants x light/dark x 3 contrast levels, as 16-bit channels (QColor's own precision).
let private oracle =
    lazy (JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "m3color-oracle.json"))).RootElement)

let private text (e: JsonElement) (name: string) = e.GetProperty(name).GetString()
let private number (e: JsonElement) (name: string) = e.GetProperty(name).GetDouble()
let private hex16 (c: Rgb) = sprintf "%04x%04x%04x" c.R c.G c.B

[<Fact>]
let ``every role matches Sung for every seed, variant, mode and contrast`` () =
    let root = oracle.Value
    let names = [ for n in root.GetProperty("roles").EnumerateArray() -> n.GetString() ]
    let mutable checkedRoles = 0
    let mismatches = ResizeArray<string>()

    for scheme in root.GetProperty("schemes").EnumerateArray() do
        let seed = (Color.tryParse (text scheme "seed")).Value
        let variant = ColorScheme.parseVariant (text scheme "variant")
        let dark = scheme.GetProperty("dark").GetBoolean()
        let contrast = number scheme "contrast"
        let expected = [ for r in scheme.GetProperty("rgb").EnumerateArray() -> r.GetString() ]
        let got = ColorScheme.build seed dark variant contrast

        List.iter2
            (fun (name: string) (want: string) ->
                checkedRoles <- checkedRoles + 1
                let have = hex16 got[name]

                if have <> want then
                    mismatches.Add(sprintf "%s/%s/dark=%b/contrast=%g %s: %s <> %s" (text scheme "seed") (text scheme "variant") dark contrast name have want))
            names
            expected

    Assert.Equal(240 * 49, checkedRoles)
    Assert.True(mismatches.Count = 0, String.Join("\n", Seq.truncate 10 mismatches))

[<Fact>]
let ``HCT measurement matches Sung exactly`` () =
    for h in oracle.Value.GetProperty("hct").EnumerateArray() do
        let color = (Color.tryParse (text h "color")).Value
        let m = Color.measure color
        Assert.Equal(number h "hue", m.Hue)
        Assert.Equal(number h "chroma", m.Chroma)
        Assert.Equal(number h "tone", m.Tone)
        Assert.Equal(number h "toneOf", Color.toneOf color)

[<Fact>]
let ``solve matches Sung exactly`` () =
    for s in oracle.Value.GetProperty("solve").EnumerateArray() do
        let args = [ for x in s.GetProperty("solve").EnumerateArray() -> x.GetDouble() ]
        let want = [ for x in s.GetProperty("rgb16").EnumerateArray() -> x.GetInt32() ]
        let c = Color.solve args[0] args[1] args[2]
        Assert.Equal<int list>(want, [ int c.R; int c.G; int c.B ])

[<Fact>]
let ``hex round trips`` () =
    for hex in [ "#000000"; "#ffffff"; "#6750a4"; "#1db954"; "#123456" ] do
        Assert.Equal(hex, Color.toHex (Color.tryParse hex).Value)

    Assert.True((Color.tryParse "#12345").IsNone)
    Assert.True((Color.tryParse "zzzzzz").IsNone)
    Assert.True((Color.tryParse null).IsNone)

[<Fact>]
let ``preferences fall back to their defaults`` () =
    Assert.Equal("system", Appearance.normalizeMode "")
    Assert.Equal("dark", Appearance.normalizeMode "dark")
    Assert.Equal("#00658e", Appearance.normalizeAccent "nope")
    Assert.Equal("#ff00aa", Appearance.normalizeAccent " #FF00aa ")
    Assert.Equal("tonalSpot", Appearance.normalizeVariant "junk")
    Assert.Equal("vibrant", Appearance.normalizeVariant "vibrant")
    Assert.Equal("standard", Appearance.normalizeContrast "junk")
    Assert.Equal(0.0, Appearance.contrastLevel "standard")
    Assert.Equal(0.5, Appearance.contrastLevel "medium")
    Assert.Equal(1.0, Appearance.contrastLevel "high")

[<Fact>]
let ``system mode follows the desktop and the others do not`` () =
    Assert.True(Appearance.isDark "system" true)
    Assert.False(Appearance.isDark "system" false)
    Assert.True(Appearance.isDark "dark" false)
    Assert.False(Appearance.isDark "light" true)

[<Fact>]
let ``tokens are a JSON object of every role`` () =
    let json = JsonDocument.Parse(Appearance.tokens "#6750a4" false "tonalSpot" "standard").RootElement
    Assert.Equal(49, json.EnumerateObject() |> Seq.length)

    for name in [ "primary"; "onPrimary"; "surface"; "surfaceContainerHigh"; "outlineVariant"; "scrim" ] do
        Assert.Matches("^#[0-9a-f]{6}$", json.GetProperty(name).GetString())

[<Fact>]
let ``light and dark give different surfaces`` () =
    let surface dark = JsonDocument.Parse(Appearance.tokens "#6750a4" dark "tonalSpot" "standard").RootElement.GetProperty("surface").GetString()
    Assert.NotEqual<string>(surface false, surface true)

[<Fact>]
let ``text on the surface keeps a readable contrast at every level`` () =
    for dark in [ false; true ] do
        for level in [ "standard"; "medium"; "high" ] do
            let json = JsonDocument.Parse(Appearance.tokens "#386a20" dark "tonalSpot" level).RootElement
            let color (name: string) = (Color.tryParse (json.GetProperty(name).GetString())).Value
            Assert.True(Color.contrastRatio (color "onSurface") (color "surface") >= 4.5)
            Assert.True(Color.contrastRatio (color "onPrimary") (color "primary") >= 4.5)
