using System.Threading;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using VideoMonitor.Core.Media;
using VideoMonitor.Wpf.Behaviors;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.ViewModels;
using VideoMonitor.Wpf.Views.Pages;

namespace VideoMonitor.Core.Tests.Views;

[Collection("Wpf")]
public sealed class MediaViewPasswordBindingTests
{
    private static readonly SemaphoreSlim WpfGate = new(1, 1);

    [Fact]
    public async Task SuccessfulTestKeepsSecretUntilImmediateSave()
    {
        await RunOnStaAsync(async () =>
        {
            var api = new RecordingMediaSettingsApiClient();
            var viewModel = new MediaSettingsViewModel(
                api,
                new Uri("https://server.example/"));
            var view = new MediaView
            {
                DataContext = viewModel,
            };
            var host = new Window
            {
                Width = 800,
                Height = 600,
                Opacity = 0,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = view,
            };

            host.Show();
            view.UpdateLayout();

            var passwordBox = FindVisualChild<PasswordBox>(view);
            Assert.NotNull(passwordBox);
            var bindingExpression = passwordBox!.GetBindingExpression(
                PasswordBoxBinding.BoundPasswordProperty);
            Assert.NotNull(bindingExpression);
            Assert.Same(viewModel, bindingExpression!.DataItem);
            Assert.Equal(
                nameof(MediaSettingsViewModel.ZlmSecret),
                bindingExpression.ParentBinding.Path.Path);
            Assert.Equal(
                BindingMode.TwoWay,
                Assert.IsType<Binding>(bindingExpression.ParentBinding).Mode);

            foreach (var value in new[] { "first-entry", "onsite-secret" })
            {
                passwordBox.Password = value;

                Assert.Equal(value, viewModel.ZlmSecret);
                Assert.NotNull(passwordBox.GetBindingExpression(
                    PasswordBoxBinding.BoundPasswordProperty));
            }

            viewModel.ZlmSecret = string.Empty;
            Assert.Empty(passwordBox.Password);

            passwordBox.Password = "onsite-secret";
            Assert.Equal("onsite-secret", viewModel.ZlmSecret);

            var commandTask = viewModel.TestCommand.ExecuteAsync(null);
            api.CompleteTest();
            await commandTask;

            Assert.Equal("onsite-secret", api.LastTestRequest?.ZlmSecret);
            Assert.Equal("onsite-secret", viewModel.ZlmSecret);
            Assert.Equal("onsite-secret", passwordBox.Password);
            Assert.Equal("配置测试成功，Secret 尚未保存。", viewModel.StatusText);
            Assert.False(viewModel.HasSecret);
            Assert.Equal(0, api.UpdateCalls);

            var saveTask = viewModel.SaveCommand.ExecuteAsync(null);
            api.CompleteSave();
            await saveTask;

            Assert.Equal("onsite-secret", api.LastUpdateRequest?.ZlmSecret);
            Assert.Empty(viewModel.ZlmSecret);
            Assert.Empty(passwordBox.Password);
            Assert.True(viewModel.HasSecret);
            Assert.Equal("流媒体设置保存成功。", viewModel.StatusText);

            view.Visibility = Visibility.Collapsed;
            view.UpdateLayout();
            view.Visibility = Visibility.Visible;
            view.UpdateLayout();
            await Task.Yield();

            Assert.True(viewModel.HasSecret);
            Assert.Empty(viewModel.ZlmSecret);
            Assert.Empty(passwordBox.Password);

            host.Close();
        });
    }

    [Fact]
    public async Task FailedTestClearsTransientSecret()
    {
        await RunOnStaAsync(async () =>
        {
            var api = new RecordingMediaSettingsApiClient
            {
                TestFailure = new CatalogApiException("AuthFailed")
            };
            var viewModel = new MediaSettingsViewModel(
                api,
                new Uri("https://server.example/"));
            var view = new MediaView
            {
                DataContext = viewModel,
            };
            var host = new Window
            {
                Width = 800,
                Height = 600,
                Opacity = 0,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = view,
            };

            host.Show();
            view.UpdateLayout();

            var passwordBox = FindVisualChild<PasswordBox>(view);
            Assert.NotNull(passwordBox);
            passwordBox!.Password = "onsite-secret";

            var commandTask = viewModel.TestCommand.ExecuteAsync(null);
            await commandTask;

            Assert.Empty(viewModel.ZlmSecret);
            Assert.Empty(passwordBox.Password);
            Assert.Equal("配置测试失败：ZLM Secret 不正确。", viewModel.StatusText);

            host.Close();
        });
    }

    [Fact]
    public async Task LeavingMediaViewClearsSuccessfulUnsavedSecret()
    {
        await RunOnStaAsync(async () =>
        {
            var api = new RecordingMediaSettingsApiClient();
            var viewModel = new MediaSettingsViewModel(
                api,
                new Uri("https://server.example/"));
            var view = new MediaView
            {
                DataContext = viewModel,
            };
            var host = new Window
            {
                Width = 800,
                Height = 600,
                Opacity = 0,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = view,
            };

            host.Show();
            view.UpdateLayout();

            var passwordBox = FindVisualChild<PasswordBox>(view);
            Assert.NotNull(passwordBox);
            passwordBox!.Password = "onsite-secret";

            var commandTask = viewModel.TestCommand.ExecuteAsync(null);
            api.CompleteTest();
            await commandTask;
            Assert.Equal("onsite-secret", passwordBox.Password);

            view.Visibility = Visibility.Collapsed;
            view.UpdateLayout();

            Assert.Empty(viewModel.ZlmSecret);
            Assert.Empty(passwordBox.Password);

            host.Close();
        });
    }

    [Fact]
    public async Task SaveCommandClearsRealMediaViewPasswordBox()
    {
        await RunOnStaAsync(async () =>
        {
            var api = new RecordingMediaSettingsApiClient();
            var viewModel = new MediaSettingsViewModel(
                api,
                new Uri("https://server.example/"));
            var view = new MediaView
            {
                DataContext = viewModel,
            };
            var host = new Window
            {
                Width = 800,
                Height = 600,
                Opacity = 0,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = view,
            };

            host.Show();
            view.UpdateLayout();

            var passwordBox = FindVisualChild<PasswordBox>(view);
            Assert.NotNull(passwordBox);
            passwordBox!.Password = "onsite-secret";
            Assert.Equal("onsite-secret", viewModel.ZlmSecret);

            var commandTask = viewModel.SaveCommand.ExecuteAsync(null);
            api.CompleteSave();
            await commandTask;

            Assert.Equal("onsite-secret", api.LastUpdateRequest?.ZlmSecret);
            Assert.True(
                string.IsNullOrEmpty(viewModel.ZlmSecret),
                "MediaSettingsViewModel did not clear ZlmSecret after Save.");
            Assert.True(
                string.IsNullOrEmpty(passwordBox.Password),
                "The real MediaView PasswordBox did not clear after Save.");

            host.Close();
        });
    }

    private static T? FindVisualChild<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static Application CreateTestApplication()
    {
        var application = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        foreach (var resourceName in new[]
        {
            "Colors.xaml",
            "Icons.xaml",
            "Typography.xaml",
            "Buttons.xaml",
            "Controls.xaml",
        })
        {
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    $"pack://application:,,,/VideoMonitor.Wpf;component/Themes/{resourceName}",
                    UriKind.Absolute),
            });
        }

        return application;
    }

    private static async Task RunOnStaAsync(Func<Task> action)
    {
        await WpfGate.WaitAsync();
        try
        {
            var completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                Exception? failure = null;
                try
                {
                    var dispatcher = Dispatcher.CurrentDispatcher;
                    SynchronizationContext.SetSynchronizationContext(
                        new DispatcherSynchronizationContext(dispatcher));
                    ResetApplicationState();
                    var application = CreateTestApplication();
                    _ = RunAsync();
                    Dispatcher.Run();
                    ResetApplicationState();

                    if (failure is null)
                    {
                        completion.SetResult(null);
                    }
                    else
                    {
                        completion.SetException(failure);
                    }

                    async Task RunAsync()
                    {
                        try
                        {
                            await action();
                        }
                        catch (Exception exception)
                        {
                            failure = exception;
                        }
                        finally
                        {
                            dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
                        }
                    }
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            })
            {
                IsBackground = true,
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            await completion.Task;
        }
        finally
        {
            WpfGate.Release();
        }
    }

    private static void ResetApplicationState()
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        typeof(Application).GetField("_appInstance", flags)!.SetValue(null, null);
        typeof(Application).GetField("_appCreatedInThisAppDomain", flags)!
            .SetValue(null, false);
        typeof(Application).GetField("_isShuttingDown", flags)!.SetValue(null, false);
    }

    private sealed class RecordingMediaSettingsApiClient : IMediaSettingsApiClient
    {
        private readonly TaskCompletionSource<MediaSettingsTestResult> testCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<MediaSettingsDto> updateCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TestMediaSettingsRequest? LastTestRequest { get; private set; }
        public UpdateMediaSettingsRequest? LastUpdateRequest { get; private set; }
        public CatalogApiException? TestFailure { get; init; }
        public bool StoredHasSecret { get; private set; }
        public int UpdateCalls { get; private set; }

        public Task<MediaSettingsDto> GetAsync(
            Uri baseUri,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaSettingsDto(
                string.Empty,
                string.Empty,
                "__defaultVhost__",
                "videomonitor",
                "videomonitor-test",
                StoredHasSecret,
                30,
                1));

        public Task<MediaSettingsDto> UpdateAsync(
            Uri baseUri,
            UpdateMediaSettingsRequest request,
            CancellationToken cancellationToken = default) =>
            RecordUpdateRequest(request);

        public Task<MediaSettingsTestResult> TestAsync(
            Uri baseUri,
            TestMediaSettingsRequest request,
            CancellationToken cancellationToken = default)
        {
            LastTestRequest = request;
            if (TestFailure is not null)
            {
                return Task.FromException<MediaSettingsTestResult>(TestFailure);
            }

            return testCompletion.Task;
        }

        public void CompleteTest() =>
            testCompletion.TrySetResult(new MediaSettingsTestResult(true, null));

        public void CompleteSave()
        {
            StoredHasSecret = true;
            updateCompletion.TrySetResult(new MediaSettingsDto(
                    string.Empty,
                    string.Empty,
                    "__defaultVhost__",
                    "videomonitor",
                    "videomonitor-test",
                    true,
                    30,
                    2));
        }

        private Task<MediaSettingsDto> RecordUpdateRequest(
            UpdateMediaSettingsRequest request)
        {
            UpdateCalls++;
            LastUpdateRequest = request;
            return updateCompletion.Task;
        }
    }
}

[CollectionDefinition("Wpf", DisableParallelization = true)]
public sealed class WpfTestCollection
{
}
