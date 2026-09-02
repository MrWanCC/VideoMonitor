using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using VideoMonitor.Core.Media;
using VideoMonitor.Wpf.Behaviors;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.Behaviors;

public sealed class PasswordBoxBindingTests
{
    [Fact]
    public void UserInputThenViewModelResetClearsPasswordBox()
    {
        RunOnSta(() =>
        {
            var draft = new DeviceEditDraftViewModel();
            var passwordBox = BindPasswordBox(draft, nameof(DeviceEditDraftViewModel.Password));
            var bindingExpression = passwordBox.GetBindingExpression(PasswordBoxBinding.BoundPasswordProperty);
            Assert.NotNull(bindingExpression);
            Assert.Same(draft, bindingExpression!.DataItem);
            Assert.Equal(nameof(DeviceEditDraftViewModel.Password), bindingExpression.ParentBinding.Path.Path);
            passwordBox.Password = "first-entry";
            Assert.NotNull(passwordBox.GetBindingExpression(PasswordBoxBinding.BoundPasswordProperty));
            Assert.Equal("first-entry", draft.Password);

            draft.Password = string.Empty;

            Assert.Empty(passwordBox.Password);
        });
    }

    [Fact]
    public void BindingRemainsActiveAfterMultipleUserEdits()
    {
        RunOnSta(() =>
        {
            var draft = new DeviceEditDraftViewModel();
            var passwordBox = BindPasswordBox(draft, nameof(DeviceEditDraftViewModel.Password));

            foreach (var value in new[] { "first-entry", "second-entry", "third-entry" })
            {
                passwordBox.Password = value;
                Assert.Equal(value, draft.Password);

                var viewModelValue = $"{value}-from-view-model";
                draft.Password = viewModelValue;
                Assert.Equal(viewModelValue, passwordBox.Password);
            }
        });
    }

    [Fact]
    public void ResetForAddAfterPreviousPasswordShowsEmptyPasswordBox()
    {
        RunOnSta(() =>
        {
            var draft = new DeviceEditDraftViewModel();
            var passwordBox = BindPasswordBox(draft, nameof(DeviceEditDraftViewModel.Password));
            passwordBox.Password = "previous-entry";

            draft.ResetForAdd(Guid.NewGuid());

            Assert.Empty(draft.Password);
            Assert.Empty(passwordBox.Password);
        });
    }

    [Fact]
    public async Task MediaSettingsSecretPasswordBoxStillPreservesBinding()
    {
        await RunOnStaAsync(async () =>
        {
            var api = new RecordingMediaSettingsApiClient();
            var viewModel = new MediaSettingsViewModel(
                api,
                new Uri("https://server.example/"));
            var passwordBox = BindPasswordBox(viewModel, nameof(MediaSettingsViewModel.ZlmSecret));

            passwordBox.Password = "first-entry";
            Assert.Equal("first-entry", viewModel.ZlmSecret);

            await viewModel.TestCommand.ExecuteAsync(null);

            Assert.Equal("first-entry", api.LastTestRequest?.ZlmSecret);

            viewModel.ZlmSecret = string.Empty;

            Assert.Empty(passwordBox.Password);
        });
    }

    private static PasswordBox BindPasswordBox(object source, string propertyName)
    {
        var passwordBox = new PasswordBox();
        BindingOperations.SetBinding(
            passwordBox,
            PasswordBoxBinding.BoundPasswordProperty,
            new Binding(propertyName)
            {
                Source = source,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
        return passwordBox;
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static async Task RunOnStaAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                _ = action().ContinueWith(
                    task =>
                    {
                        if (task.IsFaulted)
                        {
                            completion.SetException(task.Exception!.InnerException!);
                        }
                        else if (task.IsCanceled)
                        {
                            completion.SetCanceled();
                        }
                        else
                        {
                            completion.SetResult(null);
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task;
    }

    private sealed class RecordingMediaSettingsApiClient : IMediaSettingsApiClient
    {
        public TestMediaSettingsRequest? LastTestRequest { get; private set; }

        public Task<MediaSettingsDto> GetAsync(
            Uri baseUri,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaSettingsDto(
                string.Empty,
                string.Empty,
                "__defaultVhost__",
                "videomonitor",
                "videomonitor-test",
                false,
                30,
                1));

        public Task<MediaSettingsDto> UpdateAsync(
            Uri baseUri,
            UpdateMediaSettingsRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MediaSettingsTestResult> TestAsync(
            Uri baseUri,
            TestMediaSettingsRequest request,
            CancellationToken cancellationToken = default) =>
            RecordTestRequest(request);

        private Task<MediaSettingsTestResult> RecordTestRequest(TestMediaSettingsRequest request)
        {
            LastTestRequest = request;
            return Task.FromResult(new MediaSettingsTestResult(true, null));
        }
    }
}
