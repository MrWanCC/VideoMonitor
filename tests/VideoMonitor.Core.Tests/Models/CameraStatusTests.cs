using VideoMonitor.Core.Models;

namespace VideoMonitor.Core.Tests.Models;

public sealed class CameraStatusTests
{
    [Fact]
    public void Values_PreserveExistingNumbersAndAddUnknown()
    {
        Assert.Equal(0, (int)CameraStatus.Online);
        Assert.Equal(1, (int)CameraStatus.Warning);
        Assert.Equal(2, (int)CameraStatus.Offline);
        Assert.Equal(3, (int)CameraStatus.Unknown);
    }
}
