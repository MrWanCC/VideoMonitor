using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.Configuration;

namespace VideoMonitor.Wpf.ViewModels;

public sealed class ServerSettingsViewModel : ObservableObject
{
    private readonly ServerConnectionCoordinator coordinator;
    private readonly IClientSettingsStore settingsStore;
    private readonly Func<bool> hasUnsavedDraft;
    private string baseUrl;
    private bool isBusy;
    private bool isTestSuccessful;
    private string testResultText = string.Empty;
    private string saveError = string.Empty;
    private bool hasSaveError;

    public ServerSettingsViewModel(
        ServerConnectionCoordinator coordinator,
        IClientSettingsStore settingsStore,
        Func<bool> hasUnsavedDraft)
    {
        this.coordinator = coordinator
            ?? throw new ArgumentNullException(nameof(coordinator));
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        this.hasUnsavedDraft = hasUnsavedDraft
            ?? throw new ArgumentNullException(nameof(hasUnsavedDraft));

        baseUrl = settingsStore.Load().Server.BaseUrl ?? string.Empty;
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
    }

    public string BaseUrl
    {
        get => baseUrl;
        set
        {
            if (!SetProperty(ref baseUrl, value ?? string.Empty))
            {
                return;
            }

            if (isTestSuccessful)
            {
                IsTestSuccessful = false;
                TestResultText = string.Empty;
            }

            if (hasSaveError)
            {
                HasSaveError = false;
                SaveError = string.Empty;
            }
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set => SetProperty(ref isBusy, value);
    }

    public bool IsTestSuccessful
    {
        get => isTestSuccessful;
        private set => SetProperty(ref isTestSuccessful, value);
    }

    public string TestResultText
    {
        get => testResultText;
        private set => SetProperty(ref testResultText, value);
    }

    public string SaveError
    {
        get => saveError;
        private set => SetProperty(ref saveError, value);
    }

    public bool HasSaveError
    {
        get => hasSaveError;
        private set => SetProperty(ref hasSaveError, value);
    }

    public IAsyncRelayCommand TestConnectionCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    private async Task TestConnectionAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (!TryParseBaseUri(BaseUrl, out var candidate))
        {
            IsTestSuccessful = false;
            TestResultText = "请输入有效的 HTTP 或 HTTPS 服务器地址。";
            return;
        }

        IsBusy = true;
        var candidateUri = candidate!;
        try
        {
            await coordinator.ProbeAsync(candidateUri);
            if (!CurrentBaseUrlMatches(candidateUri))
            {
                ClearTestResult();
                return;
            }

            IsTestSuccessful = true;
            TestResultText = "连接测试成功。";
            HasSaveError = false;
            SaveError = string.Empty;
        }
        catch (CatalogApiException exception)
        {
            ApplyTestFailure(
                candidateUri,
                $"连接测试失败：{exception.Code}");
        }
        catch (OperationCanceledException)
        {
            ApplyTestFailure(candidateUri, "连接测试已取消。");
        }
        catch (Exception)
        {
            ApplyTestFailure(candidateUri, "连接测试失败。");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (!TryParseBaseUri(BaseUrl, out var candidate))
        {
            SetSaveFailure("请输入有效的 HTTP 或 HTTPS 服务器地址。");
            return;
        }

        if (hasUnsavedDraft())
        {
            SetSaveFailure("存在未保存的设备目录修改，请先保存或取消。");
            return;
        }

        IsBusy = true;
        var candidateUri = candidate!;
        try
        {
            await coordinator.SwitchServerAsync(candidateUri, hasUnsavedDraft);
            var authoritativeBaseUri = coordinator.Status.BaseUri;
            if (authoritativeBaseUri is null)
            {
                SetSaveFailure("服务器切换失败。");
                return;
            }

            BaseUrl = authoritativeBaseUri.ToString();
            IsTestSuccessful = false;
            TestResultText = "服务器设置保存成功。";
            HasSaveError = false;
            SaveError = string.Empty;
        }
        catch (CatalogApiException exception)
        {
            ApplySaveFailure(
                candidateUri,
                $"服务器切换失败：{exception.Code}");
        }
        catch (InvalidOperationException)
        {
            ApplySaveFailure(
                candidateUri,
                "存在未保存的设备目录修改，请先保存或取消。");
        }
        catch (OperationCanceledException)
        {
            ApplySaveFailure(candidateUri, "服务器切换已取消。");
        }
        catch (Exception)
        {
            ApplySaveFailure(candidateUri, "服务器切换失败。");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetTestFailure(string message)
    {
        IsTestSuccessful = false;
        TestResultText = message;
    }

    private void ApplyTestFailure(Uri candidate, string message)
    {
        if (CurrentBaseUrlMatches(candidate))
        {
            SetTestFailure(message);
        }
        else
        {
            ClearTestResult();
        }
    }

    private void ApplySaveFailure(Uri candidate, string message)
    {
        if (CurrentBaseUrlMatches(candidate))
        {
            SetSaveFailure(message);
        }
        else
        {
            ClearTestResult();
            HasSaveError = false;
            SaveError = string.Empty;
        }
    }

    private void ClearTestResult()
    {
        IsTestSuccessful = false;
        TestResultText = string.Empty;
    }

    private void SetSaveFailure(string message)
    {
        IsTestSuccessful = false;
        HasSaveError = true;
        SaveError = message;
    }

    private static bool TryParseBaseUri(string value, out Uri? endpoint)
    {
        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp
                || parsed.Scheme == Uri.UriSchemeHttps))
        {
            endpoint = parsed;
            return true;
        }

        endpoint = null;
        return false;
    }

    private bool CurrentBaseUrlMatches(Uri candidate) =>
        TryParseBaseUri(BaseUrl, out var current)
        && Equals(current, candidate);
}
