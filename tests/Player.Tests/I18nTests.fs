module I18nTests

open System.Text.Json
open System.Text.RegularExpressions
open Player.Core.Localization
open Xunit

[<Fact>]
let ``no key is repeated`` () =
    let keys = Translations.entries |> List.map (fun (key, _, _) -> key)
    Assert.Equal(keys.Length, (List.distinct keys).Length)

[<Fact>]
let ``no text is empty`` () =
    for key, es, en in Translations.entries do
        Assert.False(System.String.IsNullOrWhiteSpace es, key + " (es)")
        Assert.False(System.String.IsNullOrWhiteSpace en, key + " (en)")

[<Fact>]
let ``both languages fill the same placeholders`` () =
    let holes (text: string) =
        [ for m in Regex.Matches(text, @"\{\d+\}") -> m.Value ] |> List.sort

    for key, es, en in Translations.entries do
        Assert.True((holes es = holes en), key)

[<Fact>]
let ``unknown languages give English`` () =
    Assert.Equal("en", I18n.normalize "")
    Assert.Equal("en", I18n.normalize null)
    Assert.Equal("en", I18n.normalize "fr")
    Assert.Equal("es", I18n.normalize "es")

[<Fact>]
let ``a text follows the selected language`` () =
    I18n.select "en"
    Assert.Equal("Home", I18n.t "nav.home")
    I18n.select "es"
    Assert.Equal("Inicio", I18n.t "nav.home")

[<Fact>]
let ``a missing key gives the key itself`` () =
    Assert.Equal("no.such.key", I18n.t "no.such.key")

[<Fact>]
let ``tf fills the placeholders`` () =
    I18n.select "en"
    Assert.Equal("12 results", I18n.tf "search.results" [| box 12 |])
    I18n.select "es"
    Assert.Equal("12 resultados", I18n.tf "search.results" [| box 12 |])

[<Fact>]
let ``the table is a JSON object with every key`` () =
    let json = JsonDocument.Parse(I18n.tableJson "en").RootElement
    Assert.Equal(Translations.entries.Length, json.EnumerateObject() |> Seq.length)
    Assert.Equal("Queue", json.GetProperty("queue.title").GetString())
