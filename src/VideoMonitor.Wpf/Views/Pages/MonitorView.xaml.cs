using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using VideoMonitor.Wpf.Controls;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Wpf.Views.Pages;

public partial class MonitorView
{
    private const double ExpandedDetailHeight = 104;
    private const double CollapsedDetailHeight = 44;
    private IReadOnlyList<VideoTile> tileControls = [];

    public MonitorView()
    {
        InitializeComponent();
        tileControls = [MainTile1, MainTile2, MainTile3, MainTile4];
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    public void SetFullscreen(bool fullscreen)
    {
        MonitorHeader.Visibility = fullscreen
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;
        MonitorHeaderRow.Height = fullscreen
            ? new System.Windows.GridLength(0)
            : new System.Windows.GridLength(48);
        DetailPanel.Visibility = fullscreen ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        DetailRow.Height = fullscreen
            ? new System.Windows.GridLength(0)
            : GetDetailPanelHeight();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MonitorViewModel oldViewModel)
        {
            oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is MonitorViewModel newViewModel)
        {
            newViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        ApplySingleTileLayout();
        ApplyDetailPanelState();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await Dispatcher.InvokeAsync(
            static () => { },
            System.Windows.Threading.DispatcherPriority.Loaded);
        await ActivatePlaybackIfVisibleAsync();
    }

    private async void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MonitorViewModel viewModel)
        {
            await viewModel.DeactivatePlaybackAsync();
        }
    }

    private async void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            await Dispatcher.InvokeAsync(
                static () => { },
                System.Windows.Threading.DispatcherPriority.Loaded);
            await ActivatePlaybackIfVisibleAsync();
        }
        else if (DataContext is MonitorViewModel viewModel)
        {
            await viewModel.DeactivatePlaybackAsync();
        }
    }

    private async Task ActivatePlaybackIfVisibleAsync()
    {
        if (IsLoaded && IsVisible && DataContext is MonitorViewModel viewModel)
        {
            await viewModel.ActivatePlaybackAsync();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MonitorViewModel.IsSingleTileMode)
            or nameof(MonitorViewModel.SelectedVideoSlot))
        {
            ApplySingleTileLayout();
        }

        if (e.PropertyName == nameof(MonitorViewModel.IsDetailPanelCollapsed))
        {
            ApplyDetailPanelState();
        }
    }

    private void OnVideoTileMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is MonitorViewModel viewModel
            && sender is VideoTile { DataContext: VideoTileViewModel slot })
        {
            viewModel.ToggleSingleTileCommand.Execute(slot);
            e.Handled = true;
        }
    }

    private void ApplySingleTileLayout()
    {
        ResetFourTileLayout();

        if (DataContext is not MonitorViewModel viewModel || !viewModel.IsSingleTileMode)
        {
            return;
        }

        foreach (var tile in tileControls)
        {
            var isSelected = ReferenceEquals(tile.DataContext, viewModel.SelectedVideoSlot);
            tile.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;

            if (!isSelected)
            {
                continue;
            }

            Grid.SetRow(tile, 0);
            Grid.SetColumn(tile, 0);
            Grid.SetRowSpan(tile, 2);
            Grid.SetColumnSpan(tile, 2);
            System.Windows.Controls.Panel.SetZIndex(tile, 1);
        }
    }

    private void ResetFourTileLayout()
    {
        for (var index = 0; index < tileControls.Count; index++)
        {
            var tile = tileControls[index];
            tile.Visibility = Visibility.Visible;
            Grid.SetRow(tile, index / 2);
            Grid.SetColumn(tile, index % 2);
            Grid.SetRowSpan(tile, 1);
            Grid.SetColumnSpan(tile, 1);
            System.Windows.Controls.Panel.SetZIndex(tile, 0);
        }
    }

    private void ApplyDetailPanelState()
    {
        if (DetailPanel.Visibility == Visibility.Visible)
        {
            DetailRow.Height = GetDetailPanelHeight();
        }
    }

    private GridLength GetDetailPanelHeight()
    {
        var collapsed = DataContext is MonitorViewModel { IsDetailPanelCollapsed: true };
        return new GridLength(collapsed ? CollapsedDetailHeight : ExpandedDetailHeight);
    }
}
