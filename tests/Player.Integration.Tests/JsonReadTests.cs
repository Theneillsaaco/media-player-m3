using System.Text.Json;
using Player.Providers;

namespace Player.Integration.Tests;

public class JsonReadTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    [Fact]
    public void Text_reads_strings_only()
    {
        var e = Json("""{"s":"x","n":5,"z":null}""");

        Assert.Equal("x", JsonRead.Text(e, "s"));
        Assert.Equal("", JsonRead.Text(e, "n"));
        Assert.Equal("", JsonRead.Text(e, "z"));
        Assert.Equal("", JsonRead.Text(e, "missing"));
    }

    [Fact]
    public void Str_reads_numbers_as_text_too()
    {
        var e = Json("""{"s":"x","n":123456789012,"b":true}""");

        Assert.Equal("x", JsonRead.Str(e, "s"));
        Assert.Equal("123456789012", JsonRead.Str(e, "n"));
        Assert.Equal("", JsonRead.Str(e, "b"));
    }

    [Fact]
    public void Number_and_IsTrue_fall_back_quietly()
    {
        var e = Json("""{"n":2.5,"t":true,"s":"x"}""");

        Assert.Equal(2.5, JsonRead.Number(e, "n"));
        Assert.Equal(7, JsonRead.Number(e, "s", 7));
        Assert.True(JsonRead.IsTrue(e, "t"));
        Assert.False(JsonRead.IsTrue(e, "n"));
    }
}