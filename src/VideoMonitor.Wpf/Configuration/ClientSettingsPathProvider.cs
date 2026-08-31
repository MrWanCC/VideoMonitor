using System;
using System.IO;

namespace VideoMonitor.Wpf.Configuration;

public static class ClientSettingsPathProvider
{
    public static string GetPath(string? injectedRoot = null)
    {
        var root = injectedRoot is null
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "VideoMonitor",
                "Client")
            : Path.GetFullPath(injectedRoot);

        return Path.Combine(root, "client-settings.json");
    }
}
