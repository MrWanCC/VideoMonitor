using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VideoMonitor.Infrastructure.ZLMediaKit;

namespace VideoMonitor.Wpf.Configuration;

public static class LocalConfigurationLoader
{
    private const string AppSettingsFileName = "appsettings.Development.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static LocalPlaybackConfiguration Load(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        var appSettingsPath = Path.Combine(baseDirectory, AppSettingsFileName);
        if (!File.Exists(appSettingsPath))
        {
            return DisabledConfiguration();
        }

        var document = Deserialize<DevelopmentSettingsDocument>(appSettingsPath);
        var testOptions = document.SingleCameraTest ?? new SingleCameraTestOptions();
        var zlmOptions = document.Zlm ?? new ZlmOptions();
        if (!testOptions.Enabled)
        {
            return new LocalPlaybackConfiguration(testOptions, zlmOptions);
        }

        ValidateZlm(zlmOptions);
        ValidateSingleCameraTest(testOptions);
        return new LocalPlaybackConfiguration(testOptions, zlmOptions);
    }

    private static LocalPlaybackConfiguration DisabledConfiguration() => new(
        new SingleCameraTestOptions(),
        new ZlmOptions());

    private static T Deserialize<T>(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, SerializerOptions)
                ?? throw new InvalidOperationException($"配置文件{Path.GetFileName(path)}为空。");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException($"配置文件{Path.GetFileName(path)}格式无效。");
        }
    }

    private static void ValidateZlm(ZlmOptions options)
    {
        Require(options.BaseUrl, "Zlm.BaseUrl");
        Require(options.Secret, "Zlm.Secret");
        Require(options.RtspHost, "Zlm.RtspHost");
        Require(options.Vhost, "Zlm.Vhost");
        Require(options.App, "Zlm.App");

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("配置项Zlm.BaseUrl必须是有效的HTTP地址。");
        }

        ValidatePort(options.RtspPort, "Zlm.RtspPort");
    }

    private static void ValidateSingleCameraTest(SingleCameraTestOptions options)
    {
        if (options.DeviceId == Guid.Empty)
        {
            throw new InvalidOperationException("配置项SingleCameraTest.DeviceId不能为空。");
        }

        if (options.ChannelId == Guid.Empty)
        {
            throw new InvalidOperationException("配置项SingleCameraTest.ChannelId不能为空。");
        }
    }

    private static void Require(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"配置项{fieldName}不能为空。");
        }
    }

    private static void ValidatePort(int port, string fieldName)
    {
        if (port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"配置项{fieldName}必须是有效端口。");
        }
    }

    private sealed class DevelopmentSettingsDocument
    {
        public SingleCameraTestOptions? SingleCameraTest { get; set; }

        public ZlmOptions? Zlm { get; set; }
    }
}
