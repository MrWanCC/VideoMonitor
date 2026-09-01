using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoMonitor.Core.Media;
using VideoMonitor.Wpf.Catalog;

namespace VideoMonitor.Wpf.ViewModels;

public sealed class MediaSettingsViewModel : ObservableObject
{
    private readonly IMediaSettingsApiClient apiClient;
    private readonly Func<Uri?> baseUriProvider;
    private string zlmApiBaseUrl = string.Empty;
    private string playbackBaseUrl = string.Empty;
    private string vhost = "__defaultVhost__";
    private string formalApp = "videomonitor";
    private string testApp = "videomonitor-test";
    private string zlmSecret = string.Empty;
    private int noReaderGraceSeconds = 30;
    private long revision = 1;
    private bool hasSecret;
    private bool isBusy;
    private string statusText = "尚未加载流媒体设置";

    public MediaSettingsViewModel(
        IMediaSettingsApiClient apiClient,
        Uri baseUri)
        : this(apiClient, () => baseUri)
    {
    }

    public MediaSettingsViewModel(
        IMediaSettingsApiClient apiClient,
        Func<Uri?> baseUriProvider)
    {
        this.apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        this.baseUriProvider = baseUriProvider
            ?? throw new ArgumentNullException(nameof(baseUriProvider));
        TestCommand = new AsyncRelayCommand(TestAsync);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
    }

    public string ZlmApiBaseUrl
    {
        get => zlmApiBaseUrl;
        set => SetProperty(ref zlmApiBaseUrl, value ?? string.Empty);
    }

    public string PlaybackBaseUrl
    {
        get => playbackBaseUrl;
        set => SetProperty(ref playbackBaseUrl, value ?? string.Empty);
    }

    public string Vhost
    {
        get => vhost;
        set => SetProperty(ref vhost, value ?? string.Empty);
    }

    public string FormalApp
    {
        get => formalApp;
        set => SetProperty(ref formalApp, value ?? string.Empty);
    }

    public string TestApp
    {
        get => testApp;
        set => SetProperty(ref testApp, value ?? string.Empty);
    }

    public string ZlmSecret
    {
        get => zlmSecret;
        set => SetProperty(ref zlmSecret, value ?? string.Empty);
    }

    public int NoReaderGraceSeconds
    {
        get => noReaderGraceSeconds;
        set => SetProperty(ref noReaderGraceSeconds, value);
    }

    public long Revision
    {
        get => revision;
        private set => SetProperty(ref revision, value);
    }

    public bool HasSecret
    {
        get => hasSecret;
        private set => SetProperty(ref hasSecret, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set => SetProperty(ref isBusy, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public IAsyncRelayCommand TestCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var endpoint = baseUriProvider();
            if (endpoint is null)
            {
                StatusText = "中央服务器未连接。";
                return;
            }

            Apply(await apiClient.GetAsync(endpoint, cancellationToken)
                .ConfigureAwait(false));
            StatusText = "流媒体设置已加载。";
        }
        catch (CatalogApiException exception)
        {
            StatusText = $"流媒体设置加载失败：{exception.Code}";
        }
        catch (OperationCanceledException)
        {
            StatusText = "流媒体设置加载已取消。";
        }
        catch
        {
            StatusText = "流媒体设置加载失败。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task TestAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        var secret = ZlmSecret;
        try
        {
            var endpoint = baseUriProvider();
            if (endpoint is null)
            {
                StatusText = "中央服务器未连接。";
                return;
            }

            var result = await apiClient.TestAsync(
                    endpoint,
                    CreateTestRequest(secret))
                .ConfigureAwait(false);
            StatusText = result.IsReachable
                ? "配置测试成功。"
                : $"配置测试失败：{result.FailureCode}";
        }
        catch (CatalogApiException exception)
        {
            StatusText = $"配置测试失败：{exception.Code}";
        }
        catch (OperationCanceledException)
        {
            StatusText = "配置测试已取消。";
        }
        catch
        {
            StatusText = "配置测试失败。";
        }
        finally
        {
            ZlmSecret = string.Empty;
            IsBusy = false;
        }
    }

    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        var secret = ZlmSecret;
        try
        {
            var endpoint = baseUriProvider();
            if (endpoint is null)
            {
                StatusText = "中央服务器未连接。";
                return;
            }

            var saved = await apiClient.UpdateAsync(
                    endpoint,
                    new UpdateMediaSettingsRequest(
                        ZlmApiBaseUrl,
                        PlaybackBaseUrl,
                        Vhost,
                        FormalApp,
                        TestApp,
                        secret,
                        NoReaderGraceSeconds,
                        Revision))
                .ConfigureAwait(false);
            Apply(saved);
            StatusText = "流媒体设置保存成功。";
        }
        catch (CatalogApiException exception)
        {
            StatusText = $"流媒体设置保存失败：{exception.Code}";
        }
        catch (OperationCanceledException)
        {
            StatusText = "流媒体设置保存已取消。";
        }
        catch
        {
            StatusText = "流媒体设置保存失败。";
        }
        finally
        {
            ZlmSecret = string.Empty;
            IsBusy = false;
        }
    }

    private TestMediaSettingsRequest CreateTestRequest(string secret) =>
        new(
            ZlmApiBaseUrl,
            PlaybackBaseUrl,
            Vhost,
            FormalApp,
            TestApp,
            secret,
            NoReaderGraceSeconds);

    private void Apply(MediaSettingsDto dto)
    {
        ZlmApiBaseUrl = dto.ZlmApiBaseUrl;
        PlaybackBaseUrl = dto.PlaybackBaseUrl;
        Vhost = dto.Vhost;
        FormalApp = dto.FormalApp;
        TestApp = dto.TestApp;
        NoReaderGraceSeconds = dto.NoReaderGraceSeconds;
        Revision = dto.Revision;
        HasSecret = dto.HasSecret;
        ZlmSecret = string.Empty;
    }
}
