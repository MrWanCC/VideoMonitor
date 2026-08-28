using VideoMonitor.Client.Models;

namespace VideoMonitor.Client.Controls;

public sealed class VideoGridControl : UserControl
{
    private readonly List<VideoTileControl> tiles = [];

    private VideoGridControl(int rows, int columns)
    {
        BackColor = Color.FromArgb(8, 15, 26);
        Dock = DockStyle.Fill;
        Padding = new Padding(4);

        Grid = new TableLayoutPanel
        {
            BackColor = BackColor,
            Dock = DockStyle.Fill,
            RowCount = rows,
            ColumnCount = columns,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };

        Controls.Add(Grid);
    }

    public TableLayoutPanel Grid { get; }

    public IReadOnlyList<VideoTileControl> Tiles => tiles;

    public static VideoGridControl CreateMainGrid()
    {
        var control = new VideoGridControl(2, 2);
        control.Grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        control.Grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        control.Grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        control.Grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        control.AddTiles(4);
        return control;
    }

    public static VideoGridControl CreateSecondaryGrid()
    {
        var control = new VideoGridControl(1, 3);
        control.Grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        control.Grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        control.Grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        control.Grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        control.AddTiles(3);
        return control;
    }

    public void SetCameras(IReadOnlyList<CameraInfo> cameras)
    {
        ArgumentNullException.ThrowIfNull(cameras);

        if (cameras.Count != tiles.Count)
        {
            throw new ArgumentException($"当前布局需要 {tiles.Count} 路摄像头。", nameof(cameras));
        }

        for (var index = 0; index < tiles.Count; index++)
        {
            tiles[index].SetCamera(cameras[index]);
        }
    }

    private void AddTiles(int count)
    {
        for (var index = 0; index < count; index++)
        {
            var tile = new VideoTileControl();
            tiles.Add(tile);
            Grid.Controls.Add(tile, index % Grid.ColumnCount, index / Grid.ColumnCount);
        }
    }
}
