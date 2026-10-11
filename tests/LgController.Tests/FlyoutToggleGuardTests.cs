using LgController.UI;
using Microsoft.Extensions.Time.Testing;

namespace LgController.Tests;

public class FlyoutToggleGuardTests
{
    [Fact]
    public void CanOpen_BeforeAnyClose() => Assert.True(new FlyoutToggleGuard(new FakeTimeProvider()).CanOpen());

    [Fact]
    public void ClickThatClosedThePanel_DoesNotReopenIt()
    {
        var time = new FakeTimeProvider();
        var guard = new FlyoutToggleGuard(time);

        guard.MarkClosed();
        time.Advance(TimeSpan.FromMilliseconds(50));

        Assert.False(guard.CanOpen());
    }

    [Fact]
    public void LaterClick_OpensAgain()
    {
        var time = new FakeTimeProvider();
        var guard = new FlyoutToggleGuard(time);

        guard.MarkClosed();
        time.Advance(TimeSpan.FromMilliseconds(400));

        Assert.True(guard.CanOpen());
    }
}
