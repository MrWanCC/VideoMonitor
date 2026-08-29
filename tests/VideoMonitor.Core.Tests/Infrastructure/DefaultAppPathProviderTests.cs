using VideoMonitor.Infrastructure.Paths;

namespace VideoMonitor.Core.Tests.Infrastructure;

public sealed class DefaultAppPathProviderTests
{
    [Fact]
    public void ExplicitRoot_ProducesExpectedServerPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var provider = new DefaultAppPathProvider(
            new ServerStorageOptions { RootPath = root });

        Assert.Equal(Path.GetFullPath(root), provider.RootDirectory);
        Assert.Equal(Path.Combine(provider.RootDirectory, "data"), provider.DataDirectory);
        Assert.Equal(Path.Combine(provider.DataDirectory, "videomonitor.db"), provider.DatabasePath);
        Assert.Equal(Path.Combine(provider.RootDirectory, "security"), provider.SecurityDirectory);
        Assert.Equal(Path.Combine(provider.SecurityDirectory, "master-key.protected"), provider.MasterKeyPath);
        Assert.Equal(Path.Combine(provider.RootDirectory, "backups"), provider.BackupsDirectory);
        Assert.Equal(Path.Combine(provider.RootDirectory, "logs"), provider.LogsDirectory);
        Assert.Equal(Path.Combine(provider.RootDirectory, "server-settings.json"), provider.SettingsPath);
    }

    [Fact]
    public void EnsureCreated_CreatesDirectoriesButNotDataFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var provider = new DefaultAppPathProvider(
                new ServerStorageOptions { RootPath = root });
            var layout = new ServerStorageLayout(provider);

            layout.EnsureCreated();

            Assert.True(Directory.Exists(provider.RootDirectory));
            Assert.True(Directory.Exists(provider.DataDirectory));
            Assert.True(Directory.Exists(provider.SecurityDirectory));
            Assert.True(Directory.Exists(provider.BackupsDirectory));
            Assert.True(Directory.Exists(provider.LogsDirectory));
            Assert.False(File.Exists(provider.DatabasePath));
            Assert.False(File.Exists(provider.MasterKeyPath));
            Assert.False(File.Exists(provider.SettingsPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
