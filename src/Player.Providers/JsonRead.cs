using System.Text.Json;

namespace Player.Providers;

/// <summary>Forgiving readers for the JSON of services we do not control: a missing or odd field is an empty value.</summary>
internal static class JsonRead
{
    /// <summary>The string property, "" when it is missing, null or of another type.</summary>
    public static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    
    /// <summary>Like <see cref="Text"/>, but a number is read as its text too (ids that come as either).</summary>
    public static string Str(JsonElement e, string name) =>
        !e.TryGetProperty(name, out var v) ? ""
        : v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" 
        : v.ValueKind == JsonValueKind.Number ? v.GetRawText() : "";
    
    /// <summary>The non-empty strings of an array property.</summary>
    public static string[] Strings(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? [.. v.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!)
                .Where(s => s.Length > 0)]
            : [];
    
    public static bool IsTrue(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    
    /// <summary>The number property, or <paramref name="fallback"/> when it is missing or not a number.</summary>
    public static double Number(JsonElement e, string name, double fallback = 0) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;
}