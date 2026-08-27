using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Wpf.Views.Pages;

public partial class DeviceView
{
    private readonly Storyboard _drawerOpenStoryboard;
    private readonly Storyboard _drawerCloseStoryboard;

    public DeviceView()
    {
        InitializeComponent();
        _drawerOpenStoryboard = (Storyboard)Resources["DrawerOpenStoryboard"];
        _drawerCloseStoryboard = (Storyboard)Resources["DrawerCloseStoryboard"];
        _drawerCloseStoryboard.Completed += OnDrawerCloseStoryboardCompleted;
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => DetachViewModel(DataContext as DeviceManagementViewModel);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachViewModel(e.OldValue as DeviceManagementViewModel);
        if (e.NewValue is DeviceManagementViewModel viewModel)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        ApplyDrawerState(animate: false);
    }

    private void DetachViewModel(DeviceManagementViewModel? viewModel)
    {
        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeviceManagementViewModel.IsEditPanelOpen))
        {
            Dispatcher.BeginInvoke(() => ApplyDrawerState(animate: true), DispatcherPriority.Loaded);
        }
    }

    private void ApplyDrawerState(bool animate)
    {
        var isOpen = DataContext is DeviceManagementViewModel { IsEditPanelOpen: true };
        if (isOpen)
        {
            _drawerCloseStoryboard.Remove(this);
            EditorDrawer.Visibility = Visibility.Visible;
            EditorDrawer.IsHitTestVisible = true;
            DrawerShade.Visibility = Visibility.Visible;
            DrawerShade.IsHitTestVisible = true;

            if (animate)
            {
                _drawerOpenStoryboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
            }
            else
            {
                DrawerTranslate.X = 0;
                DrawerShade.Opacity = 0.15;
            }

            return;
        }

        _drawerOpenStoryboard.Remove(this);
        if (animate && EditorDrawer.Visibility == Visibility.Visible)
        {
            _drawerCloseStoryboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
        }
        else
        {
            CompleteDrawerClose();
        }
    }

    private void OnDrawerCloseStoryboardCompleted(object? sender, EventArgs e)
    {
        if (DataContext is DeviceManagementViewModel { IsEditPanelOpen: true })
        {
            return;
        }

        CompleteDrawerClose();
    }

    private void CompleteDrawerClose()
    {
        _drawerCloseStoryboard.Remove(this);
        DrawerTranslate.X = 420;
        DrawerShade.Opacity = 0;
        EditorDrawer.Visibility = Visibility.Collapsed;
        EditorDrawer.IsHitTestVisible = false;
        DrawerShade.Visibility = Visibility.Collapsed;
        DrawerShade.IsHitTestVisible = false;
    }

    private void OnDeviceViewPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape &&
            DataContext is DeviceManagementViewModel { IsEditPanelOpen: true } viewModel)
        {
            viewModel.CancelEditCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnGroupEditorLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox editor && editor.IsVisible)
        {
            editor.Focus();
            editor.SelectAll();
        }
    }

    private void OnGroupEditorKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DataContext is not DeviceManagementViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            viewModel.CommitGroupEditCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            viewModel.CancelGroupEditCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnGroupEditorLostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is DeviceManagementViewModel { EditingGroupId: not null } viewModel)
        {
            viewModel.CommitGroupEditCommand.Execute(null);
        }
    }
}
