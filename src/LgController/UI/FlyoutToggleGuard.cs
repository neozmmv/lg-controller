namespace LgController.UI;

/// <summary>
/// Clicar no ícone com o painel aberto tira o foco do painel, que se fecha, e logo depois chega o clique.
/// Sem esta guarda o painel reabriria.
/// </summary>
public sealed class FlyoutToggleGuard(TimeProvider time)
{
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(300);
    private DateTimeOffset? _closedAt;

    public void MarkClosed() => _closedAt = time.GetUtcNow();

    public bool CanOpen() => _closedAt is not { } closedAt || time.GetUtcNow() - closedAt > Window;
}
