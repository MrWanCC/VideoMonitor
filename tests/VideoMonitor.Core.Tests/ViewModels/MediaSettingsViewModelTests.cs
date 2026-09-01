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
            throw new InvalidOperationException("Test must not save.");
        }

        public Task<MediaSettingsTestResult> TestAsync(
            Uri baseUri,
            TestMediaSettingsRequest request,
            CancellationToken cancellationToken = default)
        {
            TestCalls++;
            return Task.FromResult(new MediaSettingsTestResult(true, null));
        }
    }
}
