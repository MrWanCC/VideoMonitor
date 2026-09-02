using VideoMonitor.Core.Media;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.ViewModels;

public sealed class MediaSettingsViewModelTests
{
    [Fact]
    public async Task TestDoesNotSaveOrStartCamera()
    {
        var api = new FakeMediaSettingsApiClient
        {
            Settings = new MediaSettingsDto(
                "http://127.0.0.1:8080",
                "rtsp://media.example.test:554",
                "__defaultVhost__",
                "videomonitor",
                "videomonitor-test",
                true,
                30,
                4)
        };
        var viewModel = new MediaSettingsViewModel(
            api,
            new Uri("https://server-b/"));
        await viewModel.LoadAsync();
        viewModel.ZlmSecret = "Candidate-Only-Secret";

        await viewModel.TestCommand.ExecuteAsync(null);

        Assert.Equal(1, api.TestCalls);
        Assert.Equal(0, api.UpdateCalls);
        Assert.Equal(0, api.CameraStartCalls);
        Assert.Equal("配置测试成功。", viewModel.StatusText);
        Assert.Equal(4, viewModel.Revision);
        Assert.True(viewModel.HasSecret);
        Assert.DoesNotContain("Candidate-Only-Secret", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ZLM_SECRET_REQUIRED", "请输入 ZLM Secret。")]
    [InlineData("AuthFailed", "ZLM Secret 不正确。")]
    [InlineData("MediaServerUnavailable", "无法连接流媒体服务。")]
    [InlineData("INVALID_ZLM_API_BASE_URL", "ZLM API 地址无效。")]
    [InlineData("INVALID_PLAYBACK_BASE_URL", "播放地址无效。")]
    [InlineData("UNEXPECTED_INTERNAL_CODE", "操作失败，请重试。")]
    public async Task TestFailure_MapsInternalCodeToSafeMessage(
        string code,
        string safeMessage)
    {
        var api = new FakeMediaSettingsApiClient
        {
            TestFailure = new CatalogApiException(code),
        };
        var viewModel = new MediaSettingsViewModel(
            api,
            new Uri("https://server.example/"));
        viewModel.ZlmSecret = "test-secret";

        await viewModel.TestCommand.ExecuteAsync(null);

        Assert.Equal($"配置测试失败：{safeMessage}", viewModel.StatusText);
        Assert.DoesNotContain(code, viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveFailure_MapsInternalCodeToSafeMessage()
    {
        var api = new FakeMediaSettingsApiClient
        {
            UpdateFailure = new CatalogApiException("ZLM_SECRET_REQUIRED"),
        };
        var viewModel = new MediaSettingsViewModel(
            api,
            new Uri("https://server.example/"));

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("流媒体设置保存失败：请输入 ZLM Secret。", viewModel.StatusText);
        Assert.DoesNotContain("ZLM_SECRET_REQUIRED", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestResultFailure_MapsInternalCodeToSafeMessage()
    {
        var api = new FakeMediaSettingsApiClient
        {
            TestResult = new MediaSettingsTestResult(false, "ZLM_SECRET_REQUIRED"),
        };
        var viewModel = new MediaSettingsViewModel(
            api,
            new Uri("https://server.example/"));

        await viewModel.TestCommand.ExecuteAsync(null);

        Assert.Equal("配置测试失败：请输入 ZLM Secret。", viewModel.StatusText);
        Assert.DoesNotContain("ZLM_SECRET_REQUIRED", viewModel.StatusText, StringComparison.Ordinal);
    }

    private sealed class FakeMediaSettingsApiClient : IMediaSettingsApiClient
    {
        public MediaSettingsDto Settings { get; set; } = new(
            string.Empty,
            string.Empty,
            "__defaultVhost__",
            "videomonitor",
            "videomonitor-test",
            false,
            30,
            1);

        public int TestCalls { get; private set; }
        public int UpdateCalls { get; private set; }
        public int CameraStartCalls { get; }
        public CatalogApiException? TestFailure { get; init; }
        public CatalogApiException? UpdateFailure { get; init; }
        public MediaSettingsTestResult? TestResult { get; init; }

        public Task<MediaSettingsDto> GetAsync(
            Uri baseUri,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Settings);

        public Task<MediaSettingsDto> UpdateAsync(
            Uri baseUri,
            UpdateMediaSettingsRequest request,
            CancellationToken cancellationToken = default)
        {
            UpdateCalls++;
            if (UpdateFailure is not null)
            {
                return Task.FromException<MediaSettingsDto>(UpdateFailure);
            }

            throw new InvalidOperationException("Test must not save.");
        }

        public Task<MediaSettingsTestResult> TestAsync(
            Uri baseUri,
            TestMediaSettingsRequest request,
            CancellationToken cancellationToken = default)
        {
            TestCalls++;
            if (TestFailure is not null)
            {
                return Task.FromException<MediaSettingsTestResult>(TestFailure);
            }

            return Task.FromResult(
                TestResult ?? new MediaSettingsTestResult(true, null));
        }
    }
}
