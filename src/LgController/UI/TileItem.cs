using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LgController.UI;

/// <summary>
/// One tile of the panel grid. A tile with options opens the option tray; one without toggles when pressed.
/// </summary>
public sealed partial class TileItem(string title, string glyph = "", Geometry? icon = null, bool hasOptions = true)
    : ObservableObject
{
    public string Title { get; } = title;
    /// <summary>Vector icon used instead of the glyph.</summary>
    public Geometry? Icon { get; } = icon;
    public bool HasIcon => Icon is not null;
    public bool HasOptions { get; } = hasOptions;
    public bool IsToggle => !HasOptions;
    public ObservableCollection<OptionItem> Options { get; } = [];

    [ObservableProperty] private string _glyph = glyph;
    [ObservableProperty] private string _glyphFont = OptionCatalog.IconFont;
    [ObservableProperty] private string _value = "";
    [ObservableProperty] private bool _isEnabled;
    /// <summary>Highlights the tile: a switch that is on or a timer that is running.</summary>
    [ObservableProperty] private bool _isActive;
}

public static class TileIcons
{
    public static Geometry Fan { get; } = BuildFan();

    private static Geometry BuildFan()
    {
        var blade = Geometry.Parse("M12,11 C9,9.4 7.6,5.6 9.6,3.4 C11.6,1.4 15.2,2.8 15.2,6 C15.2,8.4 13.6,10 12,11 Z");
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        for (var angle = 0; angle < 360; angle += 120)
        {
            var copy = blade.Clone();
            copy.Transform = new RotateTransform(angle, 12, 12);
            group.Children.Add(copy);
        }

        group.Children.Add(new EllipseGeometry(new Point(12, 12), 1.9, 1.9));
        group.Freeze();
        return group;
    }
}
