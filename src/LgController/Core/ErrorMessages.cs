using LgController.ThinQ;

namespace LgController.Core;

public static class ErrorMessages
{
    public static string For(ThinQException ex) => ex.Kind switch
    {
        ThinQErrorKind.Network => "Sem conexão com a internet.",
        ThinQErrorKind.Unauthorized => "Token inválido ou expirado.",
        ThinQErrorKind.DeviceOffline => "O aparelho está offline.",
        ThinQErrorKind.RateLimited => "Muitas requisições. Aguarde um instante.",
        ThinQErrorKind.CommandRejected => "O aparelho recusou o comando.",
        _ => $"Erro da LG: {ex.Message} ({ex.Code})",
    };
}
