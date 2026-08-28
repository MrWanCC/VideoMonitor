using VideoMonitor.Client.Models;

namespace VideoMonitor.Client.Controls;

public sealed class VideoTileControl : UserControl
{
    private static readonly Color SurfaceColor = Color.FromArgb(18, 27, 43);
    private static readonly Color BorderColor = Color.FromArgb(38, 72, 112);
    private static readonly Color SecondaryTextColor = Color.FromArgb(145, 165, 190);
    private static readonly Color OnlineColor = Color.FromArgb(53, 208, 127);
    private static readonly Color OfflineColor = Color.FromArgb(130, 145, 165);
    private static readonly Color ErrorColor = Color.FromArgb(255, 159, 67);

    private readonly Label cameraNameLabel = CreateLabel(11, FontStyle.Bold, Color.White);
    private readonly Label statusLabel = CreateLabel(9, FontStyle.Bold, OnlineColor);
    private readonly Label groupLabel = CreateLabel(9, FontStyle.Regular, SecondaryTextColor);
    private readonly Label channelLabel = CreateLabel(9, FontStyle.Regular, SecondaryTextColor);
    private readonly Label placeholderLabel = CreateLabel(15, FontStyle.Bold, Color.FromArgb(91, 118, 151));
    private readonly Panel videoSurface = new();

    public VideoTileControl()
    {
        BackColor = BorderColor;
        Padding = new Padding(1);
        Margin = new Padding(4);
        Dock = DockStyle.Fill;
        MinimumSize = new Size(220, 150);

        var content = new TableLayoutPanel
        {
            BackColor = SurfaceColor,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        content.Controls.Add(CreateHeader(), 0, 0);
        content.Controls.Add(CreateVideoSurface(), 0, 1);
        content.Controls.Add(CreateFooter(), 0, 2);
        Controls.Add(content);

        cameraNameLabel.Text = "未选择摄像头";
        groupLabel.Text = "--";
        channelLabel.Text = "通道 --";
        placeholderLabel.Text = "模拟视频画面";
        ShowOffline();
    }

    public string CameraNameText => cameraNameLabel.Text;

    public string GroupNameText => groupLabel.Text;

    public string ChannelText => channelLabel.Text;

    public string StatusText => statusLabel.Text;

    public string PlaceholderText => placeholderLabel.Text;

    public void SetCamera(CameraInfo camera)
    {
        ArgumentNullException.ThrowIfNull(camera);

        cameraNameLabel.Text = camera.Name;
        groupLabel.Text = camera.GroupName;
        channelLabel.Text = $"通道 {camera.ChannelNumber}";
        placeholderLabel.Text = "模拟视频画面";

        if (camera.IsOnline)
        {
            ShowOnline();
        }
        else
        {
            ShowOffline();
        }
    }

    public void ShowOnline()
    {
        statusLabel.Text = "在线";
        statusLabel.ForeColor = OnlineColor;
    }

    public void ShowOffline()
    {
        statusLabel.Text = "离线";
        statusLabel.ForeColor = OfflineColor;
    }

    public void ShowError(string message)
    {
        statusLabel.Text = "异常";
        statusLabel.ForeColor = ErrorColor;
        placeholderLabel.Text = string.IsNullOrWhiteSpace(message) ? "视频异常" : message;
    }

    private Control CreateHeader()
    {
        var header = new TableLayoutPanel
        {
            BackColor = SurfaceColor,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(14, 7, 12, 4)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        cameraNameLabel.Dock = DockStyle.Fill;
        cameraNameLabel.TextAlign = ContentAlignment.MiddleLeft;
        cameraNameLabel.AutoEllipsis = true;
        statusLabel.AutoSize = true;
        statusLabel.Anchor = AnchorStyles.Right;
        statusLabel.TextAlign = ContentAlignment.MiddleRight;

        header.Controls.Add(cameraNameLabel, 0, 0);
        header.Controls.Add(statusLabel, 1, 0);
        return header;
    }

    private Control CreateVideoSurface()
    {
        videoSurface.BackColor = Color.FromArgb(7, 13, 23);
        videoSurface.Dock = DockStyle.Fill;
        videoSurface.Margin = new Padding(10, 0, 10, 0);

        placeholderLabel.Dock = DockStyle.Fill;
        placeholderLabel.TextAlign = ContentAlignment.MiddleCenter;
        videoSurface.Controls.Add(placeholderLabel);
        return videoSurface;
    }

    private Control CreateFooter()
    {
        var footer = new TableLayoutPanel
        {
            BackColor = SurfaceColor,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(14, 5, 14, 7)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

        groupLabel.Dock = DockStyle.Fill;
        groupLabel.TextAlign = ContentAlignment.MiddleLeft;
        groupLabel.AutoEllipsis = true;
        channelLabel.Dock = DockStyle.Fill;
        channelLabel.TextAlign = ContentAlignment.MiddleRight;

        footer.Controls.Add(groupLabel, 0, 0);
        footer.Controls.Add(channelLabel, 1, 0);
        return footer;
    }

    private static Label CreateLabel(float size, FontStyle style, Color color)
    {
        return new Label
        {
            Font = new Font("Microsoft YaHei UI", size, style),
            ForeColor = color,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
    }
}
