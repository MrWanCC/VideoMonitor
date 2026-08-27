using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using VideoMonitor.Wpf.Controls;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Wpf.Views.Pages;

public partial class MonitorView
{
    private const double ExpandedDetailHeight = 132;
    private const double CollapsedDetailHeight = 44;
    private bool detailExpanded = true;
    private IReadOnlyList<VideoTile> tileControls = [];

    public MonitorView()
    {
        InitializeComponent();
        tileControls = [MainTile1, MainTile2, MainTile3, MainTile4];
        DataContextChanged += OnDataContextChanged;
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
            : new System.Windows.GridLength(detailExpanded ? ExpandedDetailHeight : CollapsedDetailHeight);
    }

    private void ToggleDetailPanel(object sender, System.Windows.RoutedEventArgs e)
    {
        detailExpanded = !detailExpanded;
        DetailRow.Height = new System.Windows.GridLength(
            detailExpanded ? ExpandedDetailHeight : CollapsedDetailHeight);
        ((System.Windows.Media.RotateTransform)DetailChevron.RenderTransform).Angle = detailExpanded ? 90 : -90;
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
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MonitorViewModel.IsSingleTileMode)
            or nameof(MonitorViewModel.SelectedVideoSlot))
        {
            ApplySingleTileLayout();
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
}
