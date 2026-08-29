# Stage 5A Server Foundation and Central Data Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新增可作为正式中心控制面的 `VideoMonitor.Server` 基础工程，并建立 Windows-first、可测试的 ProgramData 路径、Master Key/AES-GCM 敏感数据保护、SQLite 中心设备目录、一致性本机备份和健康检查基础。

**Architecture:** `VideoMonitor.Server` 是独立 ASP.NET Core/.NET 8 进程，不承载视频数据；Stage 5A 只建立 Server 与中心数据基础，不实现 StreamManager、ZLM Hook、WPF Server Playback 或设备管理 REST API。SQLite 继续通过现有 `IDeviceCatalogStore` 作为第一版快照持久化边界，整个快照在单个 SQLite Transaction 中替换；Camera Password 在写入 SQLite 前通过应用层 AES-256-GCM 加密，AES Master Key 在 Windows 上由 DPAPI LocalMachine 包装并存放于独立 security 目录。

**Tech Stack:** .NET SDK 8.0.424, .NET 8, C#, ASP.NET Core minimal hosting, `Microsoft.Data.Sqlite` 8.0.0, `Microsoft.Extensions.Hosting.WindowsServices` 8.0.0, Windows DPAPI `LocalMachine`, AES-256-GCM, xUnit 2.5.3, `Microsoft.AspNetCore.Mvc.Testing` 8.0.0.

**Spec:** `docs/superpowers/specs/2026-08-29-centralized-video-monitoring-architecture-design.md`

**Implementation baseline:** `7adb855585d5150d0a882ecf1a14af132415ab41`

## Global Constraints

- Target framework for the new Server is exactly `net8.0`; do not make the Server project Windows-only.
- The repository SDK remains `8.0.424` from `global.json`.
- Windows is the first production host. Windows-specific machine secret protection must sit behind `IMachineSecretProtector`; domain/SQLite/Server API code must not call DPAPI directly.
- Do not modify WPF UI behavior, playback behavior, ZLMediaKit behavior, `LocalZlmPlaybackSourceProvider`, `SingleCameraPlaybackCoordinator`, LibVLC lifetime, or monitor switching in Stage 5A.
- Do not implement `StreamManager`, SingleFlight, ColdStartLimiter, ZLM Hooks, Stream Reconciler, `/api/v1/playback/resolve`, or any WPF Server playback path in Stage 5A.
- Do not implement device-management REST CRUD in Stage 5A. That data-source migration belongs to a later stage.
- Do not introduce MySQL, SQL Server, PostgreSQL, Entity Framework Core, Dapper, or a separate database service. Use direct `Microsoft.Data.Sqlite`.
- V1 central persistence tables are `device_groups`, `camera_devices`, `camera_channels`, `server_settings`, and `schema_migrations`. Do not create `stream_profiles`.
- `camera_channels` uniqueness is exactly `(device_id, channel_no, stream_type)`.
- `CameraChannel.StreamId` and `CameraDevice.Status` / `CameraStatus` are runtime-only and must not be persisted in SQLite.
- Main/Sub remain represented by `CameraChannel.StreamType`; do not refactor the current domain model into `StreamProfile`.
- Keep the current JSON persistence path working and do not migrate WPF to SQLite in this stage.
- Do not implement Device Revision semantics in Stage 5A. The architecture requires revision later, but it belongs with stream invalidation/central edit semantics in a later schema migration. Do not add a fake revision that is reset by snapshot saves.
- Every SQLite catalog save must be atomic: either the complete new snapshot is visible, or the previous snapshot remains intact.
- Camera passwords must never be stored plaintext in `videomonitor.db`, backup DBs, manifests, logs, exceptions shown to users, or test artifacts committed to Git.
- Stage 5A implements only local consistent backup creation. Remote/NAS replication, restore UI, and portable Recovery Package export/import are intentionally deferred. Therefore completion of Stage 5A is not authorization for production rollout.
- The backup service must never copy `security/master-key.protected` into the normal backup directory.
- Use TDD for each behavior: write the failing test, verify RED for the intended reason, implement the minimum production behavior, verify GREEN.
- Each task is a reviewer gate and ends with its own commit. Do not combine multiple tasks into one large commit.
- Before every commit run the focused tests for that task plus `git diff --check`.
- Do not push implementation commits until the reviewer/user explicitly asks. Report each commit SHA after the task.

---

## Target File Structure

After Stage 5A, the relevant new files should be organized as follows:

```text
src/
├─ VideoMonitor.Core/
│  └─ (no new persistence implementation; existing IDeviceCatalogStore reused)
│
├─ VideoMonitor.Infrastructure/
│  ├─ Paths/
│  │  ├─ IAppPathProvider.cs
│  │  ├─ ServerStorageOptions.cs
│  │  ├─ DefaultAppPathProvider.cs
│  │  └─ ServerStorageLayout.cs
│  ├─ Security/
│  │  ├─ IMachineSecretProtector.cs
│  │  ├─ DpapiMachineSecretProtector.cs
│  │  ├─ UnsupportedMachineSecretProtector.cs
│  │  ├─ IMasterKeyProvider.cs
│  │  ├─ MasterKeyProvider.cs
│  │  ├─ ISecretProtector.cs
│  │  └─ AesGcmSecretProtector.cs
│  └─ Persistence/
│     ├─ SqliteConnectionFactory.cs
│     ├─ SqliteDatabaseInitializer.cs
│     ├─ SqliteDeviceCatalogStore.cs
│     ├─ ISqliteBackupService.cs
│     ├─ SqliteBackupManifest.cs
│     ├─ SqliteBackupResult.cs
│     └─ SqliteBackupService.cs
│
└─ VideoMonitor.Server/
   ├─ VideoMonitor.Server.csproj
   ├─ Program.cs
   └─ Hosting/
      ├─ ServerReadinessState.cs
      └─ ServerInitializationHostedService.cs

tests/
├─ VideoMonitor.Core.Tests/
│  └─ Infrastructure/
│     ├─ DefaultAppPathProviderTests.cs
│     ├─ MasterKeyProviderTests.cs
│     ├─ AesGcmSecretProtectorTests.cs
│     ├─ SqliteDatabaseInitializerTests.cs
│     ├─ SqliteDeviceCatalogStoreTests.cs
│     └─ SqliteBackupServiceTests.cs
│
└─ VideoMonitor.Server.Tests/
   ├─ VideoMonitor.Server.Tests.csproj
   ├─ ServerHealthTests.cs
   └─ TestMachineSecretProtector.cs
```

`JsonDeviceCatalogStore.cs` stays intact in Stage 5A.

---

### Task 1: Add the independent ASP.NET Core Server host

**Files:**
- Create: `src/VideoMonitor.Server/VideoMonitor.Server.csproj`
- Create: `src/VideoMonitor.Server/Program.cs`
- Create: `tests/VideoMonitor.Server.Tests/VideoMonitor.Server.Tests.csproj`
- Create: `tests/VideoMonitor.Server.Tests/ServerHealthTests.cs`
- Modify: `VideoMonitor.sln`

**Interfaces:**
- Produces: a `net8.0` ASP.NET Core executable named `VideoMonitor.Server`.
- Produces: `GET /health/live` returning HTTP 200 with JSON `{ "status": "live" }`.
- Produces: `public partial class Program` for `WebApplicationFactory<Program>`.
- The host is Windows-Service capable through `UseWindowsService`, but still runs normally from console/debugger.

- [ ] **Step 1: Create the Server and test project files without adding the live endpoint**

Create `src/VideoMonitor.Server/VideoMonitor.Server.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\VideoMonitor.Core\VideoMonitor.Core.csproj" />
    <ProjectReference Include="..\VideoMonitor.Infrastructure\VideoMonitor.Infrastructure.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices" Version="8.0.0" />
  </ItemGroup>

</Project>
```

Create an initial `src/VideoMonitor.Server/Program.cs` that starts the app but does **not** map `/health/live` yet:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "VideoMonitor.Server";
});

var app = builder.Build();

app.Run();

public partial class Program;
```

Create `tests/VideoMonitor.Server.Tests/VideoMonitor.Server.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\VideoMonitor.Server\VideoMonitor.Server.csproj" />
  </ItemGroup>

</Project>
```

Add both projects to `VideoMonitor.sln` under the existing `src` and `tests` solution folders using `dotnet sln`, not by inventing solution GUIDs by hand.

- [ ] **Step 2: Write the failing live-health test**

Create `tests/VideoMonitor.Server.Tests/ServerHealthTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace VideoMonitor.Server.Tests;

public sealed class ServerHealthTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ServerHealthTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Live_ReturnsOk()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(body);
        Assert.Equal("live", body.Status);
    }

    private sealed record HealthResponse(string Status);
}
```

- [ ] **Step 3: Run the focused test and verify RED**

Run:

```powershell
dotnet test tests/VideoMonitor.Server.Tests/VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~ServerHealthTests.Live_ReturnsOk"
```

Expected: FAIL with 404 Not Found because `/health/live` has not been mapped.

- [ ] **Step 4: Add the minimal live endpoint**

Update `Program.cs` between `Build()` and `Run()`:

```csharp
app.MapGet("/health/live", () => Results.Ok(new
{
    status = "live"
}));
```

Do not add Swagger, controllers, ZLM routes, device routes, or sample weather endpoints.

- [ ] **Step 5: Verify GREEN and solution build**

Run:

```powershell
dotnet test tests/VideoMonitor.Server.Tests/VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~ServerHealthTests.Live_ReturnsOk"
dotnet build VideoMonitor.sln
git diff --check
```

Expected: focused test passes and full solution builds.

- [ ] **Step 6: Commit Task 1**

```powershell
git add VideoMonitor.sln src/VideoMonitor.Server tests/VideoMonitor.Server.Tests
git diff --cached
git commit -m "feat: add video monitor server host"
```

Report the commit SHA and stop for review if executing task-by-task.

---

### Task 2: Add Server data-path abstraction and directory layout

**Files:**
- Create: `src/VideoMonitor.Infrastructure/Paths/IAppPathProvider.cs`
- Create: `src/VideoMonitor.Infrastructure/Paths/ServerStorageOptions.cs`
- Create: `src/VideoMonitor.Infrastructure/Paths/DefaultAppPathProvider.cs`
- Create: `src/VideoMonitor.Infrastructure/Paths/ServerStorageLayout.cs`
- Test: `tests/VideoMonitor.Core.Tests/Infrastructure/DefaultAppPathProviderTests.cs`

**Interfaces:**
- Produces `IAppPathProvider` with:
  - `RootDirectory`
  - `DataDirectory`
  - `DatabasePath`
  - `SecurityDirectory`
  - `MasterKeyPath`
  - `BackupsDirectory`
  - `LogsDirectory`
  - `SettingsPath`
- `ServerStorageOptions.RootPath` is an optional explicit override used by tests and advanced deployment.
- `ServerStorageLayout.EnsureCreated()` creates only the required directories and does not create DB, key, settings, or backup files.

- [ ] **Step 1: Write failing path tests**

Create `tests/VideoMonitor.Core.Tests/Infrastructure/DefaultAppPathProviderTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run and verify RED**

Run:

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~DefaultAppPathProviderTests"
```

Expected: compile failure because the path types do not exist.

- [ ] **Step 3: Implement the path contracts**

Create `IAppPathProvider.cs`:

```csharp
namespace VideoMonitor.Infrastructure.Paths;

public interface IAppPathProvider
{
    string RootDirectory { get; }
    string DataDirectory { get; }
    string DatabasePath { get; }
    string SecurityDirectory { get; }
    string MasterKeyPath { get; }
    string BackupsDirectory { get; }
    string LogsDirectory { get; }
    string SettingsPath { get; }
}
```

Create `ServerStorageOptions.cs`:

```csharp
namespace VideoMonitor.Infrastructure.Paths;

public sealed class ServerStorageOptions
{
    public string? RootPath { get; set; }
}
```

Create `DefaultAppPathProvider.cs`:

```csharp
namespace VideoMonitor.Infrastructure.Paths;

public sealed class DefaultAppPathProvider : IAppPathProvider
{
    public DefaultAppPathProvider(ServerStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var configuredRoot = options.RootPath;
        RootDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(configuredRoot)
                ? GetPlatformDefaultRoot()
                : configuredRoot);

        DataDirectory = Path.Combine(RootDirectory, "data");
        DatabasePath = Path.Combine(DataDirectory, "videomonitor.db");
        SecurityDirectory = Path.Combine(RootDirectory, "security");
        MasterKeyPath = Path.Combine(SecurityDirectory, "master-key.protected");
        BackupsDirectory = Path.Combine(RootDirectory, "backups");
        LogsDirectory = Path.Combine(RootDirectory, "logs");
        SettingsPath = Path.Combine(RootDirectory, "server-settings.json");
    }

    public string RootDirectory { get; }
    public string DataDirectory { get; }
    public string DatabasePath { get; }
    public string SecurityDirectory { get; }
    public string MasterKeyPath { get; }
    public string BackupsDirectory { get; }
    public string LogsDirectory { get; }
    public string SettingsPath { get; }

    private static string GetPlatformDefaultRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "VideoMonitor",
                "Server");
        }

        if (OperatingSystem.IsLinux())
        {
            return "/var/lib/videomonitor/server";
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VideoMonitor",
            "Server");
    }
}
```

Create `ServerStorageLayout.cs`:

```csharp
namespace VideoMonitor.Infrastructure.Paths;

public sealed class ServerStorageLayout
{
    private readonly IAppPathProvider paths;

    public ServerStorageLayout(IAppPathProvider paths)
    {
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(paths.RootDirectory);
        Directory.CreateDirectory(paths.DataDirectory);
        Directory.CreateDirectory(paths.SecurityDirectory);
        Directory.CreateDirectory(paths.BackupsDirectory);
        Directory.CreateDirectory(paths.LogsDirectory);
    }
}
```

- [ ] **Step 4: Verify GREEN**

Run:

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~DefaultAppPathProviderTests"
git diff --check
```

Expected: both tests pass.

- [ ] **Step 5: Commit Task 2**

```powershell
git add src/VideoMonitor.Infrastructure/Paths tests/VideoMonitor.Core.Tests/Infrastructure/DefaultAppPathProviderTests.cs
git commit -m "feat: add server storage paths"
```

Report the commit SHA.

---

### Task 3: Add Windows machine protection and durable Master Key storage

**Files:**
- Create: `src/VideoMonitor.Infrastructure/Security/IMachineSecretProtector.cs`
- Create: `src/VideoMonitor.Infrastructure/Security/DpapiMachineSecretProtector.cs`
- Create: `src/VideoMonitor.Infrastructure/Security/UnsupportedMachineSecretProtector.cs`
- Create: `src/VideoMonitor.Infrastructure/Security/IMasterKeyProvider.cs`
- Create: `src/VideoMonitor.Infrastructure/Security/MasterKeyProvider.cs`
- Test: `tests/VideoMonitor.Core.Tests/Infrastructure/MasterKeyProviderTests.cs`

**Interfaces:**
- `IMachineSecretProtector.Protect(byte[] plaintext)` returns machine-protected bytes.
- `IMachineSecretProtector.Unprotect(byte[] protectedData)` returns plaintext bytes.
- `IMasterKeyProvider.GetOrCreateAsync(CancellationToken)` returns a 32-byte key.
- The persistent file at `security/master-key.protected` contains only the machine-protected representation, never the raw 32-byte key.
- Windows implementation uses DPAPI `DataProtectionScope.LocalMachine`.
- Non-Windows implementation fails explicitly rather than silently falling back to plaintext.

- [ ] **Step 1: Write failing Master Key tests**

Create a test-only protector inside `MasterKeyProviderTests.cs`:

```csharp
private sealed class XorMachineSecretProtector : IMachineSecretProtector
{
    private const byte Mask = 0xA7;

    public byte[] Protect(byte[] plaintext) =>
        plaintext.Select(value => (byte)(value ^ Mask)).ToArray();

    public byte[] Unprotect(byte[] protectedData) =>
        protectedData.Select(value => (byte)(value ^ Mask)).ToArray();
}
```

Add tests:

```csharp
[Fact]
public async Task GetOrCreateAsync_CreatesProtected32ByteKeyAndReusesIt()
{
    var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    try
    {
        var paths = CreatePaths(root);
        new ServerStorageLayout(paths).EnsureCreated();
        var machineProtector = new XorMachineSecretProtector();

        var firstProvider = new MasterKeyProvider(paths, machineProtector);
        var first = await firstProvider.GetOrCreateAsync();

        Assert.Equal(32, first.Length);
        Assert.True(File.Exists(paths.MasterKeyPath));
        var protectedBytes = await File.ReadAllBytesAsync(paths.MasterKeyPath);
        Assert.False(first.SequenceEqual(protectedBytes));

        var secondProvider = new MasterKeyProvider(paths, machineProtector);
        var second = await secondProvider.GetOrCreateAsync();

        Assert.Equal(first, second);
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

[Fact]
public async Task GetOrCreateAsync_RejectsInvalidExistingKey()
{
    var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    try
    {
        var paths = CreatePaths(root);
        new ServerStorageLayout(paths).EnsureCreated();
        var machineProtector = new XorMachineSecretProtector();
        await File.WriteAllBytesAsync(
            paths.MasterKeyPath,
            machineProtector.Protect(new byte[8]));

        var provider = new MasterKeyProvider(paths, machineProtector);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => provider.GetOrCreateAsync());
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
```

Use this helper:

```csharp
private static IAppPathProvider CreatePaths(string root) =>
    new DefaultAppPathProvider(
        new ServerStorageOptions { RootPath = root });
```

- [ ] **Step 2: Run and verify RED**

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~MasterKeyProviderTests"
```

Expected: compile failure because security contracts do not exist.

- [ ] **Step 3: Implement the machine-protection contracts**

`IMachineSecretProtector.cs`:

```csharp
namespace VideoMonitor.Infrastructure.Security;

public interface IMachineSecretProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] protectedData);
}
```

`DpapiMachineSecretProtector.cs`:

```csharp
using System.Security.Cryptography;

namespace VideoMonitor.Infrastructure.Security;

public sealed class DpapiMachineSecretProtector : IMachineSecretProtector
{
    public byte[] Protect(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return ProtectedData.Protect(
            plaintext,
            optionalEntropy: null,
            DataProtectionScope.LocalMachine);
    }

    public byte[] Unprotect(byte[] protectedData)
    {
        ArgumentNullException.ThrowIfNull(protectedData);
        return ProtectedData.Unprotect(
            protectedData,
            optionalEntropy: null,
            DataProtectionScope.LocalMachine);
    }
}
```

`UnsupportedMachineSecretProtector.cs`:

```csharp
namespace VideoMonitor.Infrastructure.Security;

public sealed class UnsupportedMachineSecretProtector : IMachineSecretProtector
{
    private static PlatformNotSupportedException CreateException() =>
        new("当前平台尚未配置 VideoMonitor Server 的机器级密钥保护实现。");

    public byte[] Protect(byte[] plaintext) => throw CreateException();

    public byte[] Unprotect(byte[] protectedData) => throw CreateException();
}
```

- [ ] **Step 4: Implement atomic Master Key creation**

`IMasterKeyProvider.cs`:

```csharp
namespace VideoMonitor.Infrastructure.Security;

public interface IMasterKeyProvider
{
    Task<byte[]> GetOrCreateAsync(CancellationToken cancellationToken = default);
}
```

`MasterKeyProvider.cs` must:
- serialize initialization with `SemaphoreSlim`;
- generate exactly 32 random bytes using `RandomNumberGenerator.GetBytes(32)`;
- write only protected bytes;
- use a same-directory unique temp file and flush it before moving;
- delete only its own temporary file on failure;
- validate an unprotected existing key is exactly 32 bytes;
- cache the key in process memory and return clones so callers cannot mutate the cached key.

The core of `GetOrCreateAsync` should follow this shape:

```csharp
public async Task<byte[]> GetOrCreateAsync(
    CancellationToken cancellationToken = default)
{
    if (cachedKey is { } cached)
    {
        return cached.ToArray();
    }

    await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
        if (cachedKey is not null)
        {
            return cachedKey.ToArray();
        }

        byte[] key;
        if (File.Exists(paths.MasterKeyPath))
        {
            var protectedBytes = await File.ReadAllBytesAsync(
                paths.MasterKeyPath,
                cancellationToken).ConfigureAwait(false);
            key = machineProtector.Unprotect(protectedBytes);
            ValidateKey(key);
        }
        else
        {
            key = RandomNumberGenerator.GetBytes(32);
            var protectedBytes = machineProtector.Protect(key);
            await WriteProtectedKeyAtomicallyAsync(
                protectedBytes,
                cancellationToken).ConfigureAwait(false);
        }

        cachedKey = key.ToArray();
        return key.ToArray();
    }
    finally
    {
        gate.Release();
    }
}
```

Do not log key bytes or protected bytes.

- [ ] **Step 5: Add a Windows-only DPAPI roundtrip test**

```csharp
[Fact]
public void DpapiMachineSecretProtector_RoundTripsOnWindows()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    var protector = new DpapiMachineSecretProtector();
    var plaintext = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    var protectedBytes = protector.Protect(plaintext);
    var roundTrip = protector.Unprotect(protectedBytes);

    Assert.NotEqual(plaintext, protectedBytes);
    Assert.Equal(plaintext, roundTrip);
}
```

- [ ] **Step 6: Verify GREEN**

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~MasterKeyProviderTests"
git diff --check
```

- [ ] **Step 7: Commit Task 3**

```powershell
git add src/VideoMonitor.Infrastructure/Security tests/VideoMonitor.Core.Tests/Infrastructure/MasterKeyProviderTests.cs
git commit -m "feat: add protected server master key"
```

Report the commit SHA.

---

### Task 4: Add purpose-bound AES-256-GCM secret protection

**Files:**
- Create: `src/VideoMonitor.Infrastructure/Security/ISecretProtector.cs`
- Create: `src/VideoMonitor.Infrastructure/Security/AesGcmSecretProtector.cs`
- Test: `tests/VideoMonitor.Core.Tests/Infrastructure/AesGcmSecretProtectorTests.cs`

**Interfaces:**
- `ISecretProtector.ProtectAsync(string plaintext, string purpose, CancellationToken)` returns a versioned ciphertext string.
- `ISecretProtector.UnprotectAsync(string protectedValue, string purpose, CancellationToken)` returns plaintext.
- Persistent format is exactly:
  - `aesgcm:v1:<nonce-base64>:<tag-base64>:<ciphertext-base64>`
- AES key is the 32-byte Master Key.
- Nonce is 12 random bytes.
- Authentication tag is 16 bytes.
- Additional authenticated data is UTF-8 `VideoMonitor|<purpose>`.
- Empty plaintext round-trips as empty string without generating a ciphertext.
- Wrong purpose, malformed format, invalid Base64, invalid nonce/tag sizes, or failed authentication must throw a safe `InvalidDataException` that does not echo ciphertext/plaintext.

- [ ] **Step 1: Write failing tests**

Create tests covering:

```csharp
[Fact]
public async Task ProtectAsync_RoundTripsWithoutPlaintextLeak()
{
    var provider = new StubMasterKeyProvider(
        Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
    var protector = new AesGcmSecretProtector(provider);

    var cipher = await protector.ProtectAsync(
        "camera-secret",
        "camera-password:11111111111111111111111111111111");

    Assert.StartsWith("aesgcm:v1:", cipher, StringComparison.Ordinal);
    Assert.DoesNotContain("camera-secret", cipher, StringComparison.Ordinal);

    var plain = await protector.UnprotectAsync(
        cipher,
        "camera-password:11111111111111111111111111111111");

    Assert.Equal("camera-secret", plain);
}

[Fact]
public async Task ProtectAsync_UsesFreshNonce()
{
    var provider = new StubMasterKeyProvider(new byte[32]);
    var protector = new AesGcmSecretProtector(provider);

    var first = await protector.ProtectAsync("same", "purpose");
    var second = await protector.ProtectAsync("same", "purpose");

    Assert.NotEqual(first, second);
}

[Fact]
public async Task UnprotectAsync_RejectsWrongPurpose()
{
    var provider = new StubMasterKeyProvider(new byte[32]);
    var protector = new AesGcmSecretProtector(provider);
    var cipher = await protector.ProtectAsync("secret", "camera-password:a");

    await Assert.ThrowsAsync<InvalidDataException>(
        () => protector.UnprotectAsync(cipher, "camera-password:b"));
}
```

Use a test-only `StubMasterKeyProvider` that returns a clone of an exact 32-byte key.

- [ ] **Step 2: Run and verify RED**

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~AesGcmSecretProtectorTests"
```

Expected: compile failure.

- [ ] **Step 3: Implement the interface**

```csharp
namespace VideoMonitor.Infrastructure.Security;

public interface ISecretProtector
{
    Task<string> ProtectAsync(
        string plaintext,
        string purpose,
        CancellationToken cancellationToken = default);

    Task<string> UnprotectAsync(
        string protectedValue,
        string purpose,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Implement AES-GCM**

`AesGcmSecretProtector` must use:

```csharp
private const string Prefix = "aesgcm:v1:";
private const int NonceSize = 12;
private const int TagSize = 16;
```

Encryption core:

```csharp
var key = await masterKeyProvider.GetOrCreateAsync(cancellationToken)
    .ConfigureAwait(false);
var nonce = RandomNumberGenerator.GetBytes(NonceSize);
var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
var ciphertext = new byte[plaintextBytes.Length];
var tag = new byte[TagSize];
var associatedData = Encoding.UTF8.GetBytes($"VideoMonitor|{purpose}");

using var aes = new AesGcm(key, TagSize);
aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, associatedData);
```

Decryption must parse exactly five colon-separated segments:

```text
aesgcm
v1
nonce
tag
ciphertext
```

and wrap `CryptographicException`, `FormatException`, and invalid structural input as:

```csharp
throw new InvalidDataException("敏感数据解密失败。", exception);
```

Never include the protected value, plaintext, key, or purpose contents in the error message.

- [ ] **Step 5: Add malformed-input and empty-value tests**

Require:
- empty protects to empty;
- empty unprotects to empty;
- `aesgcm:v2:` is rejected;
- malformed Base64 rejected;
- 8-byte nonce rejected;
- tampered ciphertext rejected.

- [ ] **Step 6: Verify GREEN**

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~AesGcmSecretProtectorTests"
git diff --check
```

- [ ] **Step 7: Commit Task 4**

```powershell
git add src/VideoMonitor.Infrastructure/Security/ISecretProtector.cs src/VideoMonitor.Infrastructure/Security/AesGcmSecretProtector.cs tests/VideoMonitor.Core.Tests/Infrastructure/AesGcmSecretProtectorTests.cs
git commit -m "feat: add application secret protection"
```

Report the commit SHA.

---

### Task 5: Create the SQLite V1 schema and initializer

**Files:**
- Modify: `src/VideoMonitor.Infrastructure/VideoMonitor.Infrastructure.csproj`
- Create: `src/VideoMonitor.Infrastructure/Persistence/SqliteConnectionFactory.cs`
- Create: `src/VideoMonitor.Infrastructure/Persistence/SqliteDatabaseInitializer.cs`
- Test: `tests/VideoMonitor.Core.Tests/Infrastructure/SqliteDatabaseInitializerTests.cs`

**Interfaces:**
- `SqliteConnectionFactory.CreateConnection()` returns a connection configured for the Server database path with foreign keys enabled.
- `SqliteDatabaseInitializer.CurrentSchemaVersion` is `1`.
- `SqliteDatabaseInitializer.InitializeAsync` is idempotent and process-safe.
- V1 creates exactly the five architecture tables and records migration version 1.
- It configures WAL mode and does not delete/recreate an existing database.
- A database whose latest schema migration is newer than `1` must fail with `NotSupportedException`.

- [ ] **Step 1: Add the SQLite package**

Modify `src/VideoMonitor.Infrastructure/VideoMonitor.Infrastructure.csproj` and keep the existing ProtectedData reference. Add:

```xml
<PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.0" />
```

Do not add EF Core.

- [ ] **Step 2: Write failing schema tests**

Create a temp-root test that:
1. creates `DefaultAppPathProvider`;
2. calls `ServerStorageLayout.EnsureCreated`;
3. calls `InitializeAsync`;
4. queries `sqlite_master`;
5. asserts exactly these required tables exist:

```text
schema_migrations
device_groups
camera_devices
camera_channels
server_settings
```

Also assert:

```sql
SELECT MAX(version) FROM schema_migrations;
```

returns `1`.

Add an idempotence test that calls `InitializeAsync` twice and still has one migration row for version 1.

- [ ] **Step 3: Run and verify RED**

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~SqliteDatabaseInitializerTests"
```

Expected: compile failure.

- [ ] **Step 4: Implement `SqliteConnectionFactory`**

Use a `SqliteConnectionStringBuilder`:

```csharp
var builder = new SqliteConnectionStringBuilder
{
    DataSource = paths.DatabasePath,
    Mode = SqliteOpenMode.ReadWriteCreate,
    Cache = SqliteCacheMode.Shared,
    ForeignKeys = true,
    Pooling = true,
    DefaultTimeout = 5
};
```

Return a new unopened `SqliteConnection(builder.ToString())`.

- [ ] **Step 5: Implement schema migration V1**

The V1 SQL must create:

```sql
CREATE TABLE IF NOT EXISTS schema_migrations (
    version INTEGER NOT NULL PRIMARY KEY,
    applied_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS device_groups (
    id TEXT NOT NULL PRIMARY KEY,
    name TEXT NOT NULL,
    parent_id TEXT NULL,
    sort INTEGER NOT NULL,
    enabled INTEGER NOT NULL CHECK (enabled IN (0, 1)),
    FOREIGN KEY (parent_id) REFERENCES device_groups(id) ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS camera_devices (
    id TEXT NOT NULL PRIMARY KEY,
    group_id TEXT NOT NULL,
    name TEXT NOT NULL,
    ip_address TEXT NOT NULL,
    sdk_port INTEGER NOT NULL CHECK (sdk_port BETWEEN 1 AND 65535),
    rtsp_port INTEGER NOT NULL CHECK (rtsp_port BETWEEN 1 AND 65535),
    username TEXT NOT NULL,
    password_ciphertext TEXT NOT NULL,
    manufacturer TEXT NOT NULL,
    model TEXT NOT NULL,
    transport_mode TEXT NOT NULL,
    enabled INTEGER NOT NULL CHECK (enabled IN (0, 1)),
    remark TEXT NOT NULL,
    FOREIGN KEY (group_id) REFERENCES device_groups(id) ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS camera_channels (
    id TEXT NOT NULL PRIMARY KEY,
    device_id TEXT NOT NULL,
    channel_no INTEGER NOT NULL CHECK (channel_no > 0),
    channel_name TEXT NOT NULL,
    stream_type TEXT NOT NULL,
    enabled INTEGER NOT NULL CHECK (enabled IN (0, 1)),
    FOREIGN KEY (device_id) REFERENCES camera_devices(id) ON DELETE CASCADE,
    UNIQUE (device_id, channel_no, stream_type)
);

CREATE TABLE IF NOT EXISTS server_settings (
    key TEXT NOT NULL PRIMARY KEY,
    value TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL
);
```

Before/after table creation execute:

```sql
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
```

After successful V1 creation, insert:

```sql
INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc)
VALUES (1, $appliedAtUtc);
```

Use ISO-8601 UTC via `DateTimeOffset.UtcNow.ToString("O")`.

Before applying, if `MAX(version)` is greater than `CurrentSchemaVersion`, throw:

```csharp
new NotSupportedException(
    $"数据库 SchemaVersion {version} 高于当前支持版本 {CurrentSchemaVersion}。");
```

Do not delete or downgrade the DB.

- [ ] **Step 6: Add schema-column tests**

Use:

```sql
PRAGMA table_info(camera_channels);
PRAGMA table_info(camera_devices);
```

Assert:
- `camera_channels` does **not** contain `stream_id`;
- `camera_devices` does **not** contain `status`;
- no `stream_profiles` table exists.

- [ ] **Step 7: Verify GREEN**

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~SqliteDatabaseInitializerTests"
git diff --check
```

- [ ] **Step 8: Commit Task 5**

```powershell
git add src/VideoMonitor.Infrastructure/VideoMonitor.Infrastructure.csproj src/VideoMonitor.Infrastructure/Persistence/SqliteConnectionFactory.cs src/VideoMonitor.Infrastructure/Persistence/SqliteDatabaseInitializer.cs tests/VideoMonitor.Core.Tests/Infrastructure/SqliteDatabaseInitializerTests.cs
git commit -m "feat: initialize central sqlite schema"
```

Report the commit SHA.

---

### Task 6: Implement atomic encrypted `SqliteDeviceCatalogStore`

**Files:**
- Create: `src/VideoMonitor.Infrastructure/Persistence/SqliteDeviceCatalogStore.cs`
- Test: `tests/VideoMonitor.Core.Tests/Infrastructure/SqliteDeviceCatalogStoreTests.cs`

**Interfaces:**
- Implements existing `VideoMonitor.Core.Services.IDeviceCatalogStore`.
- `LoadAsync` returns a `DeviceCatalogSnapshot` with `SchemaVersion = 1`.
- Empty initialized database returns an empty snapshot, not Mock data.
- `SaveAsync` replaces the complete catalog inside one SQLite transaction.
- Camera Password uses `ISecretProtector` purpose:
  - `camera-password:{device.Id:N}`
- `CameraDevice.Status` loads as `CameraStatus.Unknown`.
- `CameraChannel.StreamId` loads as empty and is left for `StreamIdGenerator`.
- Existing JSON store is not changed or reused internally.

- [ ] **Step 1: Write a failing roundtrip test**

Create a snapshot containing:
- a root group;
- one child group;
- one camera;
- camera password `"Password-Should-Never-Appear-In-Db"`;
- `Status = CameraStatus.Online`;
- two channel rows with the same `ChannelNo = 1`, one `Main`, one `Sub`;
- non-empty runtime `StreamId` values.

Save and load through `SqliteDeviceCatalogStore`.

Assert the loaded configuration matches, except:

```csharp
Assert.Equal(CameraStatus.Unknown, loadedDevice.Status);
Assert.All(loadedDevice.Channels, channel => Assert.Equal(string.Empty, channel.StreamId));
```

Password must round-trip to the original plaintext in memory.

- [ ] **Step 2: Add a failing raw-at-rest test**

After save, query SQLite directly:

```sql
SELECT password_ciphertext FROM camera_devices LIMIT 1;
```

Assert:
- it starts with `aesgcm:v1:`;
- it does not contain the plaintext password.

Also query the file bytes as UTF-8-safe diagnostic text only for the known ASCII password:

```csharp
var databaseBytes = await File.ReadAllBytesAsync(paths.DatabasePath);
var databaseText = System.Text.Encoding.UTF8.GetString(databaseBytes);
Assert.DoesNotContain(
    "Password-Should-Never-Appear-In-Db",
    databaseText,
    StringComparison.Ordinal);
```

Do not commit a generated DB file.

- [ ] **Step 3: Run and verify RED**

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~SqliteDeviceCatalogStoreTests"
```

Expected: compile failure.

- [ ] **Step 4: Implement snapshot validation before DB mutation**

Before encryption or opening a write transaction:
- reject any snapshot with schema version other than `DeviceCatalogSnapshot.CurrentSchemaVersion`;
- construct `InMemoryDeviceCatalog` from the snapshot to reuse existing stable-ID/group/channel relationship validation;
- independently reject duplicate `(DeviceId, ChannelNo, StreamType)` combinations, because current `InMemoryDeviceCatalog` validates channel IDs but not this composite stream identity.

Use:

```csharp
var duplicateStreamIdentity = snapshot.Devices
    .SelectMany(device => device.Channels)
    .GroupBy(channel => new
    {
        channel.DeviceId,
        channel.ChannelNo,
        channel.StreamType
    })
    .FirstOrDefault(group => group.Count() > 1);

if (duplicateStreamIdentity is not null)
{
    throw new InvalidDataException(
        "同一设备、通道号和码流类型只能存在一条通道配置。");
}
```

- [ ] **Step 5: Implement `LoadAsync`**

Algorithm:

```text
Initialize schema
Open connection
Read all groups
Read all devices
Read all channels
Attach channels to device by device_id
Decrypt each password with camera-password:{deviceId:N}
Parse TransportMode and StreamType by enum name
Set Status = Unknown
Leave StreamId empty
Return SchemaVersion 1 snapshot
```

Use invariant GUID string `"N"` for DB storage and `Guid.TryParseExact(value, "N", out ...)` on load. Invalid persisted GUID/enum values must become safe `InvalidDataException`, not silently default.

- [ ] **Step 6: Implement transactionally complete `SaveAsync`**

Use a per-store `SemaphoreSlim` to serialize snapshot replacements.

Before beginning the transaction, encrypt all passwords so an encryption failure cannot partially mutate the DB.

Inside one transaction:

```sql
DELETE FROM camera_channels;
DELETE FROM camera_devices;
UPDATE device_groups SET parent_id = NULL;
DELETE FROM device_groups;
```

The parent reset is required because `device_groups.parent_id` is a self-referencing
foreign key and the snapshot replacement must not depend on SQLite choosing a child-first
delete order.

Insert every group first with `parent_id = NULL`.

After all group IDs exist, update each non-null parent:

```sql
UPDATE device_groups
SET parent_id = $parentId
WHERE id = $id;
```

Then insert devices, then channels.

Persist enums by their stable names:

```csharp
device.TransportMode.ToString()
channel.StreamType.ToString()
```

Do not persist Status or StreamId.

Commit only after every row succeeds.

- [ ] **Step 7: Add atomic failure tests**

Seed valid snapshot A. Then attempt to save snapshot B with duplicate `(device_id, channel_no, stream_type)` or an invalid group relationship.

Require:
- `SaveAsync(B)` throws;
- `LoadAsync()` still returns snapshot A;
- no partial B rows exist.

Add a test proving `CH1/Main` plus `CH1/Sub` on the same device saves successfully.

- [ ] **Step 8: Add empty database test**

After only `InitializeAsync`:

```csharp
var snapshot = await store.LoadAsync();

Assert.NotNull(snapshot);
Assert.Equal(DeviceCatalogSnapshot.CurrentSchemaVersion, snapshot.SchemaVersion);
Assert.Empty(snapshot.Groups);
Assert.Empty(snapshot.Devices);
```

- [ ] **Step 9: Verify GREEN**

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~SqliteDeviceCatalogStoreTests"
git diff --check
```

- [ ] **Step 10: Commit Task 6**

```powershell
git add src/VideoMonitor.Infrastructure/Persistence/SqliteDeviceCatalogStore.cs tests/VideoMonitor.Core.Tests/Infrastructure/SqliteDeviceCatalogStoreTests.cs
git commit -m "feat: persist central device catalog in sqlite"
```

Report the commit SHA.

---

### Task 7: Add consistent local SQLite backup snapshots

**Files:**
- Create: `src/VideoMonitor.Infrastructure/Persistence/ISqliteBackupService.cs`
- Create: `src/VideoMonitor.Infrastructure/Persistence/SqliteBackupManifest.cs`
- Create: `src/VideoMonitor.Infrastructure/Persistence/SqliteBackupResult.cs`
- Create: `src/VideoMonitor.Infrastructure/Persistence/SqliteBackupService.cs`
- Test: `tests/VideoMonitor.Core.Tests/Infrastructure/SqliteBackupServiceTests.cs`

**Interfaces:**
- `ISqliteBackupService.CreateBackupAsync(CancellationToken)` creates one new immutable local snapshot directory.
- Uses SQLite `BackupDatabase`, never ordinary `File.Copy` of the live DB.
- Backup directory contains:
  - `videomonitor.db`
  - `manifest.json`
- Manifest contains:
  - `schemaVersion`
  - `createdAtUtc`
  - `applicationVersion`
  - `databaseSha256`
- Backup does not contain `master-key.protected`.
- Stage 5A does not implement restore, remote replication, pruning policy, or automatic debounce scheduling.

- [ ] **Step 1: Define result and manifest contracts**

`SqliteBackupManifest.cs`:

```csharp
namespace VideoMonitor.Infrastructure.Persistence;

public sealed record SqliteBackupManifest(
    int SchemaVersion,
    DateTimeOffset CreatedAtUtc,
    string ApplicationVersion,
    string DatabaseSha256);
```

`SqliteBackupResult.cs`:

```csharp
namespace VideoMonitor.Infrastructure.Persistence;

public sealed record SqliteBackupResult(
    string DirectoryPath,
    string DatabasePath,
    string ManifestPath,
    string DatabaseSha256);
```

`ISqliteBackupService.cs`:

```csharp
namespace VideoMonitor.Infrastructure.Persistence;

public interface ISqliteBackupService
{
    Task<SqliteBackupResult> CreateBackupAsync(
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Write failing backup test**

Arrange a real temp SQLite database using Task 6 store and save a Camera with an encrypted password.

Call `CreateBackupAsync`.

Assert:

```csharp
Assert.True(File.Exists(result.DatabasePath));
Assert.True(File.Exists(result.ManifestPath));
Assert.False(File.Exists(
    Path.Combine(result.DirectoryPath, "master-key.protected")));
```

Deserialize `manifest.json`, compute SHA-256 over the backup DB, and require exact match.

Open the backup DB directly with `Microsoft.Data.Sqlite` and require the camera row exists.

- [ ] **Step 3: Run and verify RED**

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~SqliteBackupServiceTests"
```

- [ ] **Step 4: Implement SQLite backup creation**

Use a `SemaphoreSlim` to prevent two backups from writing the same operation concurrently.

Create a unique directory:

```csharp
var directoryName =
    $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}";
var backupDirectory = Path.Combine(paths.BackupsDirectory, directoryName);
```

Inside the new directory:
- create destination as `videomonitor.db.tmp`;
- open source via `SqliteConnectionFactory`;
- open destination via a new connection string pointing to the temp DB;
- call:

```csharp
sourceConnection.BackupDatabase(destinationConnection);
```

- close both connections;
- SHA-256 the temp DB;
- atomically rename it to `videomonitor.db`;
- write `manifest.json.tmp` with `JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }`, flush, and rename to `manifest.json`.

Use:

```csharp
Convert.ToHexString(hash).ToLowerInvariant()
```

for `databaseSha256`.

If backup fails, delete only the newly-created backup directory and preserve the live database.

- [ ] **Step 5: Add no-plaintext and no-key backup tests**

Require:
- known plaintext Camera Password does not appear in backup DB bytes;
- `security/master-key.protected` is not copied into backup directory;
- manifest does not contain passwords/ciphertext or any key bytes.

- [ ] **Step 6: Verify GREEN**

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~SqliteBackupServiceTests"
git diff --check
```

- [ ] **Step 7: Commit Task 7**

```powershell
git add src/VideoMonitor.Infrastructure/Persistence/ISqliteBackupService.cs src/VideoMonitor.Infrastructure/Persistence/SqliteBackupManifest.cs src/VideoMonitor.Infrastructure/Persistence/SqliteBackupResult.cs src/VideoMonitor.Infrastructure/Persistence/SqliteBackupService.cs tests/VideoMonitor.Core.Tests/Infrastructure/SqliteBackupServiceTests.cs
git commit -m "feat: add sqlite backup snapshots"
```

Report the commit SHA.

---

### Task 8: Wire Server initialization and readiness health

**Files:**
- Create: `src/VideoMonitor.Server/Hosting/ServerReadinessState.cs`
- Create: `src/VideoMonitor.Server/Hosting/ServerInitializationHostedService.cs`
- Modify: `src/VideoMonitor.Server/Program.cs`
- Create: `tests/VideoMonitor.Server.Tests/TestMachineSecretProtector.cs`
- Modify: `tests/VideoMonitor.Server.Tests/ServerHealthTests.cs`

**Interfaces:**
- Server startup initializes:
  1. directories;
  2. SQLite schema;
  3. Master Key.
- Initialization failure is represented as Not Ready; it must not make `/health/live` return failure.
- `/health/ready`:
  - 200 when DB and secret protection are both ready;
  - 503 otherwise;
  - response does not expose exception details, paths, keys, credentials, or connection strings.
- `VideoMonitor.Server` registers the Stage 5A central components with DI.
- Windows uses `DpapiMachineSecretProtector`.
- Non-Windows uses `UnsupportedMachineSecretProtector` until a later Linux secret-provider stage.

- [ ] **Step 1: Add failing readiness integration tests**

Use a custom `WebApplicationFactory<Program>` that injects:
- a temp `Storage:RootPath`;
- `TestMachineSecretProtector` for deterministic cross-platform tests.

`TestMachineSecretProtector.cs`:

```csharp
using System.Security.Cryptography;
using VideoMonitor.Infrastructure.Security;

namespace VideoMonitor.Server.Tests;

internal sealed class TestMachineSecretProtector : IMachineSecretProtector
{
    public byte[] Protect(byte[] plaintext) => plaintext
        .Select(value => (byte)(value ^ 0x5A))
        .ToArray();

    public byte[] Unprotect(byte[] protectedData) => protectedData
        .Select(value => (byte)(value ^ 0x5A))
        .ToArray();
}

internal sealed class FailingMachineSecretProtector : IMachineSecretProtector
{
    public byte[] Protect(byte[] plaintext) =>
        throw new CryptographicException("machine-protection-test-failure");

    public byte[] Unprotect(byte[] protectedData) =>
        throw new CryptographicException("machine-protection-test-failure");
}
```

Put this test factory in `ServerHealthTests.cs`:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VideoMonitor.Infrastructure.Security;

namespace VideoMonitor.Server.Tests;

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

        if (disposing && Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
```

Test ready behavior:

```csharp
[Fact]
public async Task Ready_ReturnsOkAfterStorageInitialization()
{
    using var factory = new TestServerFactory();
    using var client = factory.CreateClient();

    var response = await client.GetAsync("/health/ready");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.True(File.Exists(factory.DatabasePath));
    Assert.True(File.Exists(factory.MasterKeyPath));
}
```

Add:

```csharp
[Fact]
public async Task InitializationFailure_KeepsLiveUpButReadyDown()
{
    using var factory = new TestServerFactory(
        failMachineProtection: true);
    using var client = factory.CreateClient();

    var live = await client.GetAsync("/health/live");
    var ready = await client.GetAsync("/health/ready");
    var readyBody = await ready.Content.ReadAsStringAsync();

    Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    Assert.Equal(
        HttpStatusCode.ServiceUnavailable,
        ready.StatusCode);
    Assert.DoesNotContain(
        "machine-protection-test-failure",
        readyBody,
        StringComparison.Ordinal);
}
```

The failing factory uses `FailingMachineSecretProtector`; do not make the test
depend on actual DPAPI behavior.

- [ ] **Step 2: Run and verify RED**

```powershell
dotnet test tests/VideoMonitor.Server.Tests/VideoMonitor.Server.Tests.csproj
```

Expected: readiness route/DI/startup types missing.

- [ ] **Step 3: Implement `ServerReadinessState`**

Use thread-safe integer flags:

```csharp
namespace VideoMonitor.Server.Hosting;

public sealed class ServerReadinessState
{
    private int databaseReady;
    private int secretProtectionReady;

    public bool DatabaseReady => Volatile.Read(ref databaseReady) == 1;
    public bool SecretProtectionReady =>
        Volatile.Read(ref secretProtectionReady) == 1;

    public bool IsReady => DatabaseReady && SecretProtectionReady;

    public void MarkDatabaseReady() =>
        Volatile.Write(ref databaseReady, 1);

    public void MarkSecretProtectionReady() =>
        Volatile.Write(ref secretProtectionReady, 1);
}
```

Do not store the exception object in the public readiness state.

- [ ] **Step 4: Implement startup initialization**

`ServerInitializationHostedService` implements `IHostedService`.

`StartAsync`:
1. `storageLayout.EnsureCreated()`;
2. `await databaseInitializer.InitializeAsync(cancellationToken)`;
3. `readiness.MarkDatabaseReady()`;
4. `await masterKeyProvider.GetOrCreateAsync(cancellationToken)`;
5. `readiness.MarkSecretProtectionReady()`.

Catch non-cancellation exceptions, log a safe structured message, and return without rethrowing so `/health/live` can remain available:

```csharp
logger.LogError(
    "VideoMonitor Server initialization failed. ExceptionType={ExceptionType}",
    exception.GetType().Name);
```

Do not pass the exception object to this log statement in Stage 5A, because this startup path can include filesystem/security errors and the health contract intentionally stays generic.

`StopAsync` returns `Task.CompletedTask`.

- [ ] **Step 5: Register all Stage 5A services in `Program.cs`**

Add:

```csharp
builder.Services.AddSingleton(new ServerStorageOptions
{
    RootPath = builder.Configuration["Storage:RootPath"]
});

builder.Services.AddSingleton<IAppPathProvider, DefaultAppPathProvider>();
builder.Services.AddSingleton<ServerStorageLayout>();

if (OperatingSystem.IsWindows())
{
    builder.Services.AddSingleton<
        IMachineSecretProtector,
        DpapiMachineSecretProtector>();
}
else
{
    builder.Services.AddSingleton<
        IMachineSecretProtector,
        UnsupportedMachineSecretProtector>();
}

builder.Services.AddSingleton<IMasterKeyProvider, MasterKeyProvider>();
builder.Services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();
builder.Services.AddSingleton<SqliteConnectionFactory>();
builder.Services.AddSingleton<SqliteDatabaseInitializer>();
builder.Services.AddSingleton<IDeviceCatalogStore, SqliteDeviceCatalogStore>();
builder.Services.AddSingleton<ISqliteBackupService, SqliteBackupService>();
builder.Services.AddSingleton<ServerReadinessState>();
builder.Services.AddHostedService<ServerInitializationHostedService>();
```

Add the required `using` statements for Core/Infrastructure/Server namespaces.

Do **not** register `InMemoryDeviceCatalog`, WPF types, ZLM client, Playback types, or StreamManager yet.

- [ ] **Step 6: Add `/health/ready`**

Map:

```csharp
app.MapGet(
    "/health/ready",
    (ServerReadinessState readiness) =>
    {
        if (readiness.IsReady)
        {
            return Results.Ok(new
            {
                status = "ready",
                databaseReady = true,
                secretProtectionReady = true
            });
        }

        return Results.Json(
            new
            {
                status = "not-ready",
                databaseReady = readiness.DatabaseReady,
                secretProtectionReady = readiness.SecretProtectionReady
            },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    });
```

Keep `/health/live` independent of readiness.

- [ ] **Step 7: Verify Server tests**

```powershell
dotnet test tests/VideoMonitor.Server.Tests/VideoMonitor.Server.Tests.csproj
```

Require:
- live 200;
- ready 200 on successful initialization;
- DB/key files are created under the injected temp root;
- live stays 200 and ready becomes 503 on secret initialization failure;
- response contains no exception detail.

- [ ] **Step 8: Commit Task 8**

```powershell
git add src/VideoMonitor.Server tests/VideoMonitor.Server.Tests
git diff --check
git commit -m "feat: initialize central server services"
```

Report the commit SHA.

---

### Task 9: Full Stage 5A verification gate

**Files:**
- Verification only. Do not expand production scope during this task.

**Interfaces:**
- No new interface.
- This task proves Stage 5A is reviewable as one bounded delivery.

- [ ] **Step 1: Confirm scope with Git**

Run:

```powershell
git status
git log --oneline -10
git diff 7adb855585d5150d0a882ecf1a14af132415ab41..HEAD --stat
```

Expected changed production areas:
- new Server project;
- Infrastructure Paths/Security/SQLite persistence;
- tests;
- solution/package references.

Unexpected WPF/Playback/ZLM behavior changes are a failure.

- [ ] **Step 2: Scan source diff for forbidden secret output**

Run repository searches:

```powershell
git diff 7adb855585d5150d0a882ecf1a14af132415ab41..HEAD | Select-String -Pattern "Console\.Write|Password|master-key|rtsp://|ZlmSecret"
```

Review every hit manually.

Allowed:
- model/property names;
- encryption purpose identifiers;
- tests asserting plaintext absence;
- protected key path names.

Forbidden:
- logging plaintext password;
- logging raw RTSP URL with credentials;
- writing Master Key bytes to normal logs/config;
- returning secret values from health endpoints.

- [ ] **Step 3: Run whitespace/build/test verification**

Run exactly:

```powershell
git diff --check
dotnet restore VideoMonitor.sln
dotnet build VideoMonitor.sln --no-restore
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --no-build
dotnet test tests/VideoMonitor.Client.Tests/VideoMonitor.Client.Tests.csproj --no-build
dotnet test tests/VideoMonitor.Server.Tests/VideoMonitor.Server.Tests.csproj --no-build
```

If a `--no-build` test project was not built by the solution, run its test once without `--no-build`; do not hide a test failure by skipping it.

- [ ] **Step 4: Perform the manual Windows-first smoke check**

On a Windows development machine, use a temporary explicit root rather than the real production ProgramData directory:

```powershell
$env:Storage__RootPath = "$env:TEMP\VideoMonitor-Stage5A-Smoke"
dotnet run --project src\VideoMonitor.Server\VideoMonitor.Server.csproj
```

Verify:
- `/health/live` returns 200;
- `/health/ready` returns 200;
- `$env:TEMP\VideoMonitor-Stage5A-Smoke\data\videomonitor.db` exists;
- `$env:TEMP\VideoMonitor-Stage5A-Smoke\security\master-key.protected` exists;
- no plaintext Camera Password is written anywhere by the smoke run.

Stop the Server and remove only the temporary smoke root.

- [ ] **Step 5: Report Stage 5A evidence**

Return:
- all Task commit SHAs in order;
- exact full-test counts;
- `git status`;
- list of added NuGet packages and versions;
- confirmation that WPF behavior was not modified;
- confirmation that JSON persistence remains intact;
- confirmation that StreamManager/ZLM Hooks/playback resolve were not started;
- any known follow-up that belongs to Stage 5B+.

Do **not** push automatically. Stop for architecture/reviewer approval.

---

## Stage 5A Acceptance Criteria

Stage 5A is complete only when all of the following are true:

```text
VideoMonitor.Server builds as net8.0
Server can run as console and is Windows-Service capable
/health/live works independently of readiness
/health/ready reflects DB + secret initialization
Server data root is configurable and defaults to ProgramData on Windows
SQLite V1 schema exists with no stream_profiles table
camera_channels enforces (device_id, channel_no, stream_type)
CameraStatus is not persisted
StreamId is not persisted
Camera Password is AES-GCM ciphertext at rest
AES Master Key is 32 random bytes and is not stored plaintext
Windows Master Key wrapper uses DPAPI LocalMachine
SQLite snapshot save is transactional
CH1/Main and CH1/Sub can coexist
invalid replacement leaves previous catalog intact
local backup uses SQLite BackupDatabase
backup has SHA-256 manifest
normal backup excludes master-key.protected
existing JSON/WPF persistence continues unchanged
full solution build passes
all existing tests plus new Stage 5A tests pass
```

## Explicit Deferrals After Stage 5A

The next plans must handle these separately:

```text
Stage 5B:
  old WPF JSON/DPAPI -> central SQLite migration
  all-or-nothing import
  migration verification
  portable recovery design implementation as required before rollout

Stage 5C:
  DeviceRevision semantics
  ZLM control moved behind Server
  StreamKey / StreamEntry / SingleFlight
  MediaReady verification
  ColdStartLimiter

Stage 5D:
  ZLM hooks
  none-reader close decision
  Reconciler
  restart/stale-state recovery

Stage 5E/F:
  WPF ServerPlaybackSourceResolver
  process PlaybackManager
  4+3 independent local players
  AssignmentVersion + cancellation

Stage 5G+:
  device-management API/data-source switch
  system status UI
  deployment/installer
  remote backup/restore/recovery operations
  performance/failure/soak acceptance
```

Do not pull any of those responsibilities forward into Stage 5A.
