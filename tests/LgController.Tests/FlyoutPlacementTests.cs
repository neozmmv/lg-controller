using System.Windows;
using LgController.UI;

namespace LgController.Tests;

public class FlyoutPlacementTests
{
    private static readonly Size Window = new(360, 500);

    [Fact]
    public void TaskbarAtBottom_AnchorsBottomRight() =>
        Assert.Equal(new Point(1548, 528), FlyoutPlacement.Compute(new Rect(0, 0, 1920, 1040), Window));

    [Fact]
    public void TaskbarAtTop_AnchorsTopRight() =>
        Assert.Equal(new Point(1548, 52), FlyoutPlacement.Compute(new Rect(0, 40, 1920, 1040), Window));

    [Fact]
    public void TaskbarAtLeft_AnchorsBottomLeft() =>
        Assert.Equal(new Point(72, 568), FlyoutPlacement.Compute(new Rect(60, 0, 1860, 1080), Window));

    [Fact]
    public void WindowTallerThanWorkArea_StaysInsideTop() =>
        Assert.Equal(12, FlyoutPlacement.Compute(new Rect(0, 0, 800, 400), Window).Y);
}
