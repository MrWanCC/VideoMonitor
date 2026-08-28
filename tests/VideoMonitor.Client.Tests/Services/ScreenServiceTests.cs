using VideoMonitor.Client.Services;

namespace VideoMonitor.Client.Tests.Services;

public sealed class ScreenServiceTests
{
    [Fact]
    public void CalculateSecondaryBounds_UsesSecondWorkingAreaAndFixedHeight()
    {
        var areas = new[]
        {
            new Rectangle(0, 0, 1920, 1040),
            new Rectangle(1920, 40, 2560, 1400)
        };

        var bounds = ScreenService.CalculateSecondaryBounds(areas);

        Assert.Equal(new Rectangle(1920, 40, 2560, 540), bounds);
    }

    [Fact]
    public void CalculateSecondaryBounds_SingleScreenUsesSafeTestWindow()
    {
        var bounds = ScreenService.CalculateSecondaryBounds(
            [new Rectangle(0, 0, 1920, 1040)]);

        Assert.Equal(new Rectangle(80, 80, 1440, 540), bounds);
    }
}
