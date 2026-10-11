using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace LgController.UI;

/// <summary>Efeitos de janela do Windows 11 via DWM.</summary>
public static class Backdrop
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;
    private const int CornerRound = 2;
    private const int BackdropAcrylic = 3;
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x80;

    /// <summary>Os fundos de sistema existem a partir do Windows 11 22H2.</summary>
    public static bool IsSupported => Environment.OSVersion.Version.Build >= 22621;

    /// <summary>Aplica acrílico, cantos arredondados e modo escuro. Falso quando o sistema não suporta.</summary>
    public static bool Apply(Window window, bool dark)
    {
        if (!IsSupported) return false;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return false;
        if (HwndSource.FromHwnd(handle)?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;

        Set(handle, DwmwaUseImmersiveDarkMode, dark ? 1 : 0);
        Set(handle, DwmwaWindowCornerPreference, CornerRound);
        return Set(handle, DwmwaSystemBackdropType, BackdropAcrylic);
    }

    public static void HideFromAltTab(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowLong(handle, GwlExStyle, GetWindowLong(handle, GwlExStyle) | WsExToolWindow);
    }

    private static bool Set(IntPtr handle, int attribute, int value) =>
        DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int)) == 0;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
