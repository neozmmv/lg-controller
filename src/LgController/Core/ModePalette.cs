namespace LgController.Core;

/// <summary>Accent color for each mode, used by the panel and the tray icon.</summary>
public static class ModePalette
{
    public static (byte R, byte G, byte B) For(string? mode, bool isOn)
    {
        if (!isOn) return (0x6B, 0x72, 0x80);
        return mode switch
        {
            "HEAT" => (0xFF, 0x8A, 0x3D),
            "AIR_DRY" => (0x2C, 0xC5, 0xB5),
            "FAN" => (0x8A, 0x93, 0xA0),
            "AUTO" => (0x7C, 0x6C, 0xFF),
            "AIR_CLEAN" => (0x4C, 0xC3, 0x6B),
            _ => (0x3B, 0x9E, 0xFF),
        };
    }
}
