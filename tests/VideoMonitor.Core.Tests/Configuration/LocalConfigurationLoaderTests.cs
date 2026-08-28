using VideoMonitor.Core.Models;
using VideoMonitor.Wpf.Configuration;

namespace VideoMonitor.Core.Tests.Configuration;

public sealed class LocalConfigurationLoaderTests
{
    [Fact]
    public void Load_WhenConfigurationIsAbsent_DefaultsToDisabled()
    {
        using var directory = new TemporaryDirectory();

        var configuration = LocalConfigurationLoader.Load(directory.Path);

        Assert.False(configuration.SingleCameraTest.Enabled);
    }

    [Fact]
    public void Load_WhenDisabled_DoesNotRequireSensitiveDeviceFile()
    {
        using var directory = new TemporaryDirectory();
        directory.Write(
            "appsettings.Development.json",
            """{"SingleCameraTest":{"Enabled":false}}""");

        var configuration = LocalConfigurationLoader.Load(directory.Path);

        Assert.False(configuration.SingleCameraTest.Enabled);
        Assert.Null(configuration.Device);
    }

    [Fact]
    public void Load_WhenEnabled_ParsesDiscreteCameraAndZlmFields()
    {
        using var directory = ValidConfigurationDirectory();

        var configuration = LocalConfigurationLoader.Load(directory.Path);

        Assert.True(configuration.SingleCameraTest.Enabled);
        Assert.Equal("http://192.0.2.10", configuration.Zlm.BaseUrl);
        Assert.Equal("192.0.2.10", configuration.Zlm.RtspHost);
        Assert.Equal("192.0.2.20", configuration.Device!.IpAddress);
        Assert.Equal("camera001", configuration.Device.LocalIdentifier);
        Assert.Equal(StreamType.Main, configuration.Device.StreamType);
        Assert.DoesNotContain("SourceUrl", directory.Read("local-device.json"));
    }

    [Fact]
    public void Load_WhenEnabledAndSecretMissing_ThrowsWithoutLeakingPassword()
    {
        using var directory = ValidConfigurationDirectory();
        directory.Write(
            "appsettings.Development.json",
            """
            {
              "SingleCameraTest": { "Enabled": true },
              "Zlm": {
                "BaseUrl": "http://192.0.2.10",
                "Secret": "",
                "Vhost": "__defaultVhost__",
                "App": "live",
                "RtspHost": "192.0.2.10",
                "RtspPort": 554
              }
            }
            """);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            LocalConfigurationLoader.Load(directory.Path));

        Assert.Contains("Zlm.Secret", exception.Message);
        Assert.DoesNotContain("camera-password", exception.Message);
    }

    private static TemporaryDirectory ValidConfigurationDirectory()
    {
        var directory = new TemporaryDirectory();
        directory.Write(
            "appsettings.Development.json",
            """
            {
              "SingleCameraTest": { "Enabled": true },
              "Zlm": {
                "BaseUrl": "http://192.0.2.10",
                "Secret": "example-secret",
                "Vhost": "__defaultVhost__",
                "App": "live",
                "RtspHost": "192.0.2.10",
                "RtspPort": 554
              }
            }
            """);
        directory.Write(
            "local-device.json",
            """
            {
              "LocalIdentifier": "camera001",
              "IpAddress": "192.0.2.20",
              "RtspPort": 554,
              "Username": "admin",
              "Password": "camera-password",
              "ChannelNo": 1,
              "StreamType": "Main"
            }
            """);
        return directory;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"VideoMonitor.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Write(string fileName, string content) =>
            File.WriteAllText(System.IO.Path.Combine(Path, fileName), content);

        public string Read(string fileName) =>
            File.ReadAllText(System.IO.Path.Combine(Path, fileName));

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
