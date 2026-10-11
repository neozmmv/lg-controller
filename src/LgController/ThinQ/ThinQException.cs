namespace LgController.ThinQ;

public enum ThinQErrorKind { Network, Unauthorized, DeviceOffline, RateLimited, CommandRejected, Unknown }

public sealed class ThinQException(string code, string message, ThinQErrorKind kind, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;
    public ThinQErrorKind Kind { get; } = kind;

    // Codes from the ThinQAPIErrorCodes table in the official SDK.
    public static ThinQErrorKind KindFor(string code, int httpStatus) => code switch
    {
        "1103" or "1218" or "1302" => ThinQErrorKind.Unauthorized,
        "1222" => ThinQErrorKind.DeviceOffline,
        "1306" => ThinQErrorKind.RateLimited,
        "1223" or "2205" or "2207" or "2208" or "2209" or "2301" or "2302" or "2303" or "2304" or "2305"
            => ThinQErrorKind.CommandRejected,
        _ when httpStatus == 401 => ThinQErrorKind.Unauthorized,
        _ when httpStatus == 429 => ThinQErrorKind.RateLimited,
        _ => ThinQErrorKind.Unknown,
    };
}
