using CommunityToolkit.Mvvm.ComponentModel;

namespace LgController.UI;

/// <summary>One option of a panel selector (mode, speed, timer).</summary>
public sealed partial class OptionItem(string value, string label, string glyph = "", string glyphFont = OptionCatalog.IconFont)
    : ObservableObject
{
    public string Value { get; } = value;
    public string Label { get; } = label;
    public string Glyph { get; } = glyph;
    public string GlyphFont { get; } = glyphFont;

    [ObservableProperty] private bool _isSelected;
}

public static class OptionCatalog
{
    public const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";
    private const string SymbolFont = "Segoe UI Symbol";

    public static OptionItem Mode(string value) => value switch
    {
        "COOL" => new(value, "Cool", ""),
        "HEAT" => new(value, "Heat", ""),
        "AIR_DRY" => new(value, "Dry", ""),
        "FAN" => new(value, "Fan", "≋", SymbolFont),
        "AUTO" => new(value, "Auto", ""),
        "AIR_CLEAN" => new(value, "Purify", "✦", SymbolFont),
        "ENERGY_SAVING" => new(value, "Eco", "☘", SymbolFont),
        _ => new(value, Humanize(value), "●", SymbolFont),
    };

    public static OptionItem Wind(string value) => new(value, value switch
    {
        "SLOW" => "Lowest",
        "LOW" => "Low",
        "LOW_MID" => "Low+",
        "MID" => "Medium",
        "MID_HIGH" => "Medium+",
        "HIGH" => "High",
        "POWER" => "Turbo",
        "AUTO" => "Auto",
        "NATURE" => "Natural",
        _ => Humanize(value),
    });

    public static OptionItem Timer(int minutes) => new(minutes.ToString(), minutes switch
    {
        <= 0 => "Off",
        < 60 => $"{minutes} min",
        _ => $"{minutes / 60} h",
    });

    private static string Humanize(string value)
    {
        var text = value.Replace('_', ' ').ToLowerInvariant();
        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
