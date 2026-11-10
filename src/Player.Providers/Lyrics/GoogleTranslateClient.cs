using System.Text;
using System.Text.Json;
using Player.Core.Contracts;

namespace Player.Providers.Lyrics;

/// <summary>
/// Machine translation of lyrics through the free Google Translate endpoint (unofficial: it can change or limit use).
/// The lines go in one request; if the service merges or splits lines, they are asked one by one.
/// </summary>
public sealed class GoogleTranslateClient : ILineTranslator, IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    
    public async Task<string[]> Translate(string[] lines, string language)
    {
        var result = new string[lines.Length];
        Array.Fill(result, "");

        var index = Enumerable.Range(0, lines.Length).Where(i => lines[i].Trim().Length > 0).ToArray();
        if (index.Length == 0)
            return result;

        var whole = await Request(string.Join("\n", index.Select(i => lines[i].Replace('\n', ' '))), language);
        var parts = whole.TrimEnd('\n').Split('\n');

        if (parts.Length == index.Length)
        {
            for (var k = 0; k < index.Length; k++)
                result[index[k]] = parts[k].Trim();

            return result;
        }

        using var gate = new SemaphoreSlim(4);
        await Task.WhenAll(index.Select(async i =>
        {
            await gate.WaitAsync();
            try { result[i] = (await Request(lines[i], language)).Trim(); }
            finally { gate.Release(); }
        }));

        return result;
    }

    public void Dispose() => _http.Dispose();

    private async Task<string> Request(string text, string language)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["q"] = text });
        using var response = await _http.PostAsync(
            $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={Uri.EscapeDataString(language)}&dt=t", content);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var text2 = new StringBuilder();
        foreach (var segment in doc.RootElement[0].EnumerateArray())
            if (segment.ValueKind == JsonValueKind.Array && segment.GetArrayLength() > 0 && segment[0].ValueKind == JsonValueKind.String)
                text2.Append(segment[0].GetString());

        return text2.ToString();
    }
}