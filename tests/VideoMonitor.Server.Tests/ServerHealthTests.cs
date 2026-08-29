using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VideoMonitor.Infrastructure.Security;

namespace VideoMonitor.Server.Tests;

public sealed class ServerHealthTests
{
    [Fact]
    public async Task Live_ReturnsOk()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(body);
        Assert.Equal("live", body.Status);
    }

    [Fact]
    public async Task Ready_ReturnsOkAfterStorageInitialization()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ReadinessResponse>();
        Assert.NotNull(body);
        Assert.Equal("ready", body.Status);
        Assert.True(body.DatabaseReady);
        Assert.True(body.SecretProtectionReady);
        Assert.True(File.Exists(factory.DatabasePath));
        Assert.True(File.Exists(factory.MasterKeyPath));
    }

    [Fact]
    public async Task InitializationFailure_KeepsLiveUpButReadyDown()
    {
        using var factory = new TestServerFactory(failMachineProtection: true);
        using var client = factory.CreateClient();

        var liveResponse = await client.GetAsync("/health/live");
        var readyResponse = await client.GetAsync("/health/ready");
        var readyBody = await readyResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readyResponse.StatusCode);
        Assert.Contains("\"status\":\"not-ready\"", readyBody, StringComparison.Ordinal);
        Assert.Contains("\"databaseReady\":true", readyBody, StringComparison.Ordinal);
        Assert.Contains("\"secretProtectionReady\":false", readyBody, StringComparison.Ordinal);
        Assert.DoesNotContain("machine-protection-test-failure", readyBody, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.RootPath, readyBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("videomonitor.db", readyBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("master-key.protected", readyBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Data Source=", readyBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.", readyBody, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", readyBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ready_ResponseContainsOnlyHealthState()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ciphertext", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("master-key", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rtsp://", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Data Source=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(factory.RootPath, body, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record HealthResponse(string Status);

    private sealed record ReadinessResponse(
        string Status,
        bool DatabaseReady,
        bool SecretProtectionReady);
}

internal sealed class TestServerFactory : WebApplicationFactory<Program>
{
    private readonly bool failMachineProtection;

    public TestServerFactory(bool failMachineProtection = false)
    {
        this.failMachineProtection = failMachineProtection;
        RootPath = Path.Combine(
            Path.GetTempPath(),
            "VideoMonitor.Server.Tests",
            Guid.NewGuid().ToString("N"));
    }

    public string RootPath { get; }

    public string DatabasePath =>
        Path.Combine(RootPath, "data", "videomonitor.db");

    public string MasterKeyPath =>
        Path.Combine(RootPath, "security", "master-key.protected");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Storage:RootPath", RootPath);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Storage:RootPath"] = RootPath
                });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMachineSecretProtector>();
            services.AddSingleton<IMachineSecretProtector>(
                failMachineProtection
                    ? new FailingMachineSecretProtector()
                    : new TestMachineSecretProtector());
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (Directory.Exists(RootPath))
        {
            try
            {
                Directory.Delete(RootPath, recursive: true);
            }
            catch (IOException)
            {
                // The test host may still release a file handle asynchronously.
            }
        }
    }
}
