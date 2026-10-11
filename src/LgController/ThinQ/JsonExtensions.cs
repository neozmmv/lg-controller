using System.Text.Json;

namespace LgController.ThinQ;

/// <summary>Reads that never throw: a missing path returns an undefined element.</summary>
public static class JsonExtensions
{
    public static JsonElement Prop(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    public static string? AsString(this JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    public static double? AsDouble(this JsonElement element) =>
        element.ValueKind == JsonValueKind.Number ? element.GetDouble() : null;

    public static bool? AsBool(this JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    public static IReadOnlyList<JsonElement> Items(this JsonElement element) =>
        element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().ToArray() : [];
}
