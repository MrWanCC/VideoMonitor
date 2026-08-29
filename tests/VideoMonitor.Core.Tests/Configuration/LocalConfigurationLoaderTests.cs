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
        Assert.Equal(
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            configuration.SingleCameraTest.DeviceId);
        Assert.Equal(
            Guid.Parse("60000000-0000-0000-0000-000000000001"),
            configuration.SingleCameraTest.ChannelId);
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
        Assert.Equal(
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            configuration.SingleCameraTest.DeviceId);
    }

    [Fact]
    public void Load_WhenEnabledWithoutLegacyDeviceFile_ParsesIdsAndZlmFields()
    {
        using var directory = ValidConfigurationDirectory();

        var configuration = LocalConfigurationLoader.Load(directory.Path);

        Assert.True(configuration.SingleCameraTest.Enabled);
        Assert.Equal(
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            configuration.SingleCameraTest.DeviceId);
        Assert.Equal(
            Guid.Parse("60000000-0000-0000-0000-000000000001"),
            configuration.SingleCameraTest.ChannelId);
        Assert.Equal("http://192.0.2.10", configuration.Zlm.BaseUrl);
        Assert.Equal("192.0.2.10", configuration.Zlm.RtspHost);
        Assert.False(File.Exists(Path.Combine(directory.Path, "local-device.json")));
    }

    [Fact]
    public void Load_WhenEnabledAndSecretMissing_ThrowsWithoutLeakingPassword()
    {
        using var directory = ValidConfigurationDirectory();
        directory.Write(
            "appsettings.Development.json",
            """
            {
              "SingleCameraTest": {
                "Enabled": true,
                "DeviceId": "50000000-0000-0000-0000-000000000001",
                "ChannelId": "60000000-0000-0000-0000-000000000001"
              },
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

    [Fact]
    public void Load_WhenEnabledAndDeviceIdIsEmpty_Throws()
    {
        using var directory = ValidConfigurationDirectory();
        directory.Write(
            "appsettings.Development.json",
            ValidConfigurationJson(deviceId: Guid.Empty));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            LocalConfigurationLoader.Load(directory.Path));

        Assert.Contains("SingleCameraTest.DeviceId", exception.Message);
    }

    [Fact]
    public void Load_WhenEnabledAndChannelIdIsEmpty_Throws()
    {
        using var directory = ValidConfigurationDirectory();
        directory.Write(
            "appsettings.Development.json",
            ValidConfigurationJson(channelId: Guid.Empty));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            LocalConfigurationLoader.Load(directory.Path));

        Assert.Contains("SingleCameraTest.ChannelId", exception.Message);
    }

    [Fact]
    public void Load_WhenEnabled_IgnoresLegacyDeviceFile()
    {
        using var directory = ValidConfigurationDirectory();
        directory.Write("local-device.json", "not valid json");

        var configuration = LocalConfigurationLoader.Load(directory.Path);

        Assert.True(configuration.SingleCameraTest.Enabled);
    }

    private static TemporaryDirectory ValidConfigurationDirectory()
    {
        var directory = new TemporaryDirectory();
        directory.Write("appsettings.Development.json", ValidConfigurationJson());
        return directory;
    }

    private static string ValidConfigurationJson(
        Guid? deviceId = null,
        Guid? channelId = null) => $$"""
        {
          "SingleCameraTest": {
            "Enabled": true,
            "DeviceId": "{{deviceId ?? Guid.Parse("50000000-0000-0000-0000-000000000001")}}",
            "ChannelId": "{{channelId ?? Guid.Parse("60000000-0000-0000-0000-000000000001")}}"
          },
          "Zlm": {
            "BaseUrl": "http://192.0.2.10",
            "Secret": "example-secret",
            "Vhost": "__defaultVhost__",
            "App": "live",
            "RtspHost": "192.0.2.10",
            "RtspPort": 554
          }
        }
        """;

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
