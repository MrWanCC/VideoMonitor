using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Wpf.Views.Pages;

public partial class DeviceView
{
    public DeviceView()
    {
        InitializeComponent();
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

        ApplyDrawerLayout();
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
            Dispatcher.BeginInvoke(ApplyDrawerLayout, DispatcherPriority.Loaded);
        }
    }

    private void OnDeviceLayoutSizeChanged(object sender, SizeChangedEventArgs e) => ApplyDrawerLayout();

    private void ApplyDrawerLayout()
    {
        var isOpen = DataContext is DeviceManagementViewModel { IsEditPanelOpen: true };
        if (!isOpen)
        {
            DrawerColumn.Width = new GridLength(0);
            return;
        }

        if (ActualWidth >= 1360)
        {
            DrawerColumn.Width = new GridLength(380);
            Grid.SetColumn(EditorDrawer, 2);
            System.Windows.Controls.Panel.SetZIndex(EditorDrawer, 0);
        }
        else
        {
            DrawerColumn.Width = new GridLength(0);
            Grid.SetColumn(EditorDrawer, 1);
            System.Windows.Controls.Panel.SetZIndex(EditorDrawer, 20);
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
