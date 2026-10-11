using LgController.ThinQ;

namespace LgController.Core;

public static class ErrorMessages
{
    public static string For(ThinQException ex) => ex.Kind switch
    {
        ThinQErrorKind.Network => "No internet connection.",
        ThinQErrorKind.Unauthorized => "Invalid or expired token.",
        ThinQErrorKind.DeviceOffline => "The device is offline.",
        ThinQErrorKind.RateLimited => "Too many requests. Please wait a moment.",
        ThinQErrorKind.CommandRejected => "The device rejected the command.",
        _ => $"LG error: {ex.Message} ({ex.Code})",
    };
}
