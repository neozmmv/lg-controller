using System.Windows;
using Microsoft.Win32;

namespace LgController.UI;

/// <summary>Keeps the theme dictionary in sync with the Windows app theme.</summary>
public static class ThemeManager
{
    private const string PersonalizeKey =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static event Action? Changed;

    public static bool IsDark { get; private set; } = true;

    public static void Initialize(Application app)
    {
        Apply(app);
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General) app.Dispatcher.BeginInvoke(() => Apply(app));
        };
    }

    private static void Apply(Application app)
    {
        IsDark = Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", 1) is int light && light == 0;
        var name = IsDark ? "Dark" : "Light";
        app.Resources.MergedDictionaries[0] = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/UI/Themes/{name}.xaml"),
        };
        Changed?.Invoke();
    }
}
