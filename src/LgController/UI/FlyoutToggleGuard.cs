namespace LgController.UI;

/// <summary>
/// Clicking the icon while the panel is open takes focus from the panel, which closes, and then the click arrives.
/// Without this guard the panel would reopen.
/// </summary>
public sealed class FlyoutToggleGuard(TimeProvider time)
{
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(300);
    private DateTimeOffset? _closedAt;

    public void MarkClosed() => _closedAt = time.GetUtcNow();

    public bool CanOpen() => _closedAt is not { } closedAt || time.GetUtcNow() - closedAt > Window;
}
