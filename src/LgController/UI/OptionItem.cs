using CommunityToolkit.Mvvm.ComponentModel;

namespace LgController.UI;

/// <summary>Uma opção de um seletor do painel (modo, velocidade, timer).</summary>
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
        "COOL" => new(value, "Frio", ""),
        "HEAT" => new(value, "Aquecer", ""),
        "AIR_DRY" => new(value, "Secar", ""),
        "FAN" => new(value, "Ventilar", "≋", SymbolFont),
        "AUTO" => new(value, "Auto", ""),
        "AIR_CLEAN" => new(value, "Purificar", "✦", SymbolFont),
        "ENERGY_SAVING" => new(value, "Economia", "☘", SymbolFont),
        _ => new(value, Humanize(value), "●", SymbolFont),
    };

    public static OptionItem Wind(string value) => new(value, value switch
    {
        "SLOW" => "Mínima",
        "LOW" => "Baixa",
        "LOW_MID" => "Baixa+",
        "MID" => "Média",
        "MID_HIGH" => "Média+",
        "HIGH" => "Alta",
        "POWER" => "Turbo",
        "AUTO" => "Auto",
        "NATURE" => "Natural",
        _ => Humanize(value),
    });

    public static OptionItem Timer(int minutes) => new(minutes.ToString(), minutes switch
    {
        <= 0 => "Desativado",
        < 60 => $"{minutes} min",
        _ => $"{minutes / 60} h",
    });

    private static string Humanize(string value)
    {
        var text = value.Replace('_', ' ').ToLowerInvariant();
        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
