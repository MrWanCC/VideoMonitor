using AntButton = AntdUI.Button;
using VideoMonitor.Client.Controls;
using VideoMonitor.Client.Models;
using VideoMonitor.Client.Services;

namespace VideoMonitor.Client.Forms;

public sealed class SecondaryMonitorForm : Form
{
    private static readonly Color BackgroundColor = Color.FromArgb(8, 15, 26);
    private static readonly Color PanelColor = Color.FromArgb(14, 24, 39);
    private static readonly Color AccentColor = Color.FromArgb(45, 124, 246);

    private readonly MonitorSwitchService switchService;
    private readonly IReadOnlyList<MonitorGroup> groups;
    private readonly VideoGridControl videoGrid = VideoGridControl.CreateSecondaryGrid();
    private readonly AntButton shaft2Button;
    private readonly AntButton shaft3Button;

    public SecondaryMonitorForm(
        MonitorSwitchService switchService,
        IReadOnlyList<MonitorGroup> groups)
    {
        this.switchService = switchService;
        this.groups = groups;

        Text = "卸矿站监控";
        BackColor = BackgroundColor;
        ForeColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 10f);
        ShowInTaskbar = true;
        Height = 540;

        shaft2Button = CreateGroupButton("2#主溜井");
        shaft3Button = CreateGroupButton("3#主溜井");

        Controls.Add(CreateLayout());
        switchService.LayoutChanged += OnLayoutChanged;
        Render(switchService.Current);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        switchService.LayoutChanged -= OnLayoutChanged;
        base.OnFormClosed(e);
    }

    private Control CreateLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = BackgroundColor,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(CreateHeader(), 0, 0);
        root.Controls.Add(videoGrid, 0, 1);
        return root;
    }

    private Control CreateHeader()
    {
        var header = new FlowLayoutPanel
        {
            BackColor = PanelColor,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(18, 10, 10, 8)
        };

        header.Controls.Add(new Label
        {
            AutoSize = false,
            Width = 150,
            Height = 38,
            Text = "卸矿站监控",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
            ForeColor = Color.White
        });
        header.Controls.Add(shaft2Button);
        header.Controls.Add(shaft3Button);
        return header;
    }

    private AntButton CreateGroupButton(string groupName)
    {
        var button = new AntButton
        {
            Text = groupName,
            Tag = groupName,
            Width = 128,
            Height = 36,
            Margin = new Padding(6, 1, 0, 0),
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(29, 46, 69)
        };
        button.Click += OnGroupButtonClick;
        return button;
    }

    private void OnGroupButtonClick(object? sender, EventArgs e)
    {
        if (sender is not Control { Tag: string groupName })
        {
            return;
        }

        var group = groups.Single(item => item.Name == groupName);
        switchService.SwitchUnloadingGroup(group);
    }

    private void OnLayoutChanged(object? sender, MonitorLayoutSnapshot snapshot) => Render(snapshot);

    private void Render(MonitorLayoutSnapshot snapshot)
    {
        if (IsDisposed)
        {
            return;
        }

        videoGrid.SetCameras(snapshot.SecondarySlots);
        var selectedGroup = snapshot.SecondarySlots[0].GroupName;
        SetButtonSelected(shaft2Button, selectedGroup == "2#主溜井");
        SetButtonSelected(shaft3Button, selectedGroup == "3#主溜井");
    }

    private static void SetButtonSelected(AntButton button, bool selected)
    {
        button.BackColor = selected ? AccentColor : Color.FromArgb(29, 46, 69);
    }
}
