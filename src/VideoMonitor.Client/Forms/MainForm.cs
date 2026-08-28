using AntButton = AntdUI.Button;
using VideoMonitor.Client.Controls;
using VideoMonitor.Client.Models;
using VideoMonitor.Client.Services;

namespace VideoMonitor.Client.Forms;

public sealed class MainForm : Form
{
    private static readonly Color BackgroundColor = Color.FromArgb(8, 15, 26);
    private static readonly Color PanelColor = Color.FromArgb(14, 24, 39);
    private static readonly Color AccentColor = Color.FromArgb(45, 124, 246);
    private static readonly Color MutedTextColor = Color.FromArgb(145, 165, 190);

    private readonly MonitorSwitchService switchService;
    private readonly IReadOnlyList<MonitorGroup> groups;
    private readonly ScreenService screenService;
    private readonly VideoGridControl mainVideoGrid = VideoGridControl.CreateMainGrid();
    private readonly TableLayoutPanel rootLayout = new();
    private readonly TableLayoutPanel contentLayout = new();
    private readonly Panel headerPanel = new();
    private readonly Panel footerPanel = new();
    private readonly Panel navigationPanel = new();
    private readonly Panel treePanel = new();

    private SecondaryMonitorForm? secondaryForm;
    private FormBorderStyle savedBorderStyle;
    private FormWindowState savedWindowState;
    private Rectangle savedBounds;
    private bool isMonitorFullscreen;

    public MainForm(
        MonitorSwitchService switchService,
        IReadOnlyList<MonitorGroup> groups,
        ScreenService screenService)
    {
        this.switchService = switchService;
        this.groups = groups;
        this.screenService = screenService;

        Text = "矿山视频监控平台";
        BackColor = BackgroundColor;
        ForeColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 10f);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1200, 720);
        Size = new Size(1600, 920);
        KeyPreview = true;

        ConfigureRootLayout();
        Controls.Add(rootLayout);

        switchService.LayoutChanged += OnLayoutChanged;
        mainVideoGrid.SetCameras(switchService.Current.MainSlots);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (secondaryForm is null || secondaryForm.IsDisposed)
        {
            secondaryForm = new SecondaryMonitorForm(switchService, groups);
            screenService.ConfigureSecondaryWindow(secondaryForm);
            secondaryForm.Show();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        switchService.LayoutChanged -= OnLayoutChanged;

        if (secondaryForm is { IsDisposed: false })
        {
            secondaryForm.Close();
        }

        base.OnFormClosed(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && isMonitorFullscreen)
        {
            ExitMonitorFullscreen();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ConfigureRootLayout()
    {
        rootLayout.Dock = DockStyle.Fill;
        rootLayout.BackColor = BackgroundColor;
        rootLayout.ColumnCount = 1;
        rootLayout.RowCount = 3;
        rootLayout.Margin = Padding.Empty;
        rootLayout.Padding = Padding.Empty;
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        ConfigureContentLayout();
        rootLayout.Controls.Add(CreateHeader(), 0, 0);
        rootLayout.Controls.Add(contentLayout, 0, 1);
        rootLayout.Controls.Add(CreateFooter(), 0, 2);
    }

    private void ConfigureContentLayout()
    {
        contentLayout.Dock = DockStyle.Fill;
        contentLayout.BackColor = BackgroundColor;
        contentLayout.ColumnCount = 3;
        contentLayout.RowCount = 1;
        contentLayout.Margin = Padding.Empty;
        contentLayout.Padding = Padding.Empty;
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));

        contentLayout.Controls.Add(CreateNavigation(), 0, 0);
        contentLayout.Controls.Add(mainVideoGrid, 1, 0);
        contentLayout.Controls.Add(CreateMonitorTree(), 2, 0);
    }

    private Control CreateHeader()
    {
        headerPanel.BackColor = PanelColor;
        headerPanel.Dock = DockStyle.Fill;
        headerPanel.Padding = new Padding(22, 10, 18, 10);

        var title = new Label
        {
            Dock = DockStyle.Left,
            Width = 420,
            Text = "矿山视频监控平台  /  实时监控",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold),
            ForeColor = Color.White
        };

        var fullscreenButton = new AntButton
        {
            Dock = DockStyle.Right,
            Width = 106,
            Text = "全屏",
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = AccentColor
        };
        fullscreenButton.Click += (_, _) => EnterMonitorFullscreen();

        headerPanel.Controls.Add(fullscreenButton);
        headerPanel.Controls.Add(title);
        return headerPanel;
    }

    private Control CreateNavigation()
    {
        navigationPanel.BackColor = PanelColor;
        navigationPanel.Dock = DockStyle.Fill;
        navigationPanel.Padding = new Padding(12, 18, 12, 12);

        var menu = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = PanelColor
        };

        var menuItems = new[]
        {
            "实时监控", "设备管理", "流媒体管理", "录像回放", "告警中心", "系统配置"
        };

        foreach (var item in menuItems)
        {
            menu.Controls.Add(CreateNavigationButton(item, item == "实时监控"));
        }

        navigationPanel.Controls.Add(menu);
        return navigationPanel;
    }

    private static AntButton CreateNavigationButton(string text, bool selected)
    {
        return new AntButton
        {
            Text = text,
            Width = 164,
            Height = 46,
            Margin = new Padding(0, 0, 0, 8),
            Font = new Font("Microsoft YaHei UI", 10f, selected ? FontStyle.Bold : FontStyle.Regular),
            ForeColor = selected ? Color.White : MutedTextColor,
            BackColor = selected ? AccentColor : PanelColor
        };
    }

    private Control CreateMonitorTree()
    {
        treePanel.BackColor = PanelColor;
        treePanel.Dock = DockStyle.Fill;
        treePanel.Padding = new Padding(0, 0, 0, 8);

        var heading = new Label
        {
            Dock = DockStyle.Top,
            Height = 54,
            Padding = new Padding(18, 0, 0, 0),
            Text = "监控设备",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold),
            ForeColor = Color.White
        };

        var monitorTree = new MonitorTreeControl(groups) { Dock = DockStyle.Fill };
        monitorTree.GroupSelected += OnGroupSelected;

        treePanel.Controls.Add(monitorTree);
        treePanel.Controls.Add(heading);
        return treePanel;
    }

    private Control CreateFooter()
    {
        footerPanel.BackColor = Color.FromArgb(10, 19, 31);
        footerPanel.Dock = DockStyle.Fill;

        footerPanel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 0, 0, 0),
            Text = "● 系统在线    模拟数据模式",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            ForeColor = Color.FromArgb(53, 208, 127)
        });
        return footerPanel;
    }

    private void OnGroupSelected(object? sender, MonitorGroup group)
    {
        switch (group.Type)
        {
            case MonitorGroupType.Shaft:
                switchService.SwitchShaftGroup(group);
                break;
            case MonitorGroupType.Tunnel:
                switchService.SwitchTunnel(group);
                break;
            case MonitorGroupType.UnloadingStation:
                switchService.SwitchUnloadingGroup(group);
                break;
        }
    }

    private void OnLayoutChanged(object? sender, MonitorLayoutSnapshot snapshot)
    {
        if (!IsDisposed)
        {
            mainVideoGrid.SetCameras(snapshot.MainSlots);
        }
    }

    private void EnterMonitorFullscreen()
    {
        if (isMonitorFullscreen)
        {
            return;
        }

        savedBorderStyle = FormBorderStyle;
        savedWindowState = WindowState;
        savedBounds = Bounds;
        isMonitorFullscreen = true;

        headerPanel.Visible = false;
        footerPanel.Visible = false;
        navigationPanel.Visible = false;
        treePanel.Visible = false;
        rootLayout.RowStyles[0].Height = 0;
        rootLayout.RowStyles[2].Height = 0;
        contentLayout.ColumnStyles[0].Width = 0;
        contentLayout.ColumnStyles[2].Width = 0;
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Maximized;
        mainVideoGrid.Focus();
    }

    private void ExitMonitorFullscreen()
    {
        if (!isMonitorFullscreen)
        {
            return;
        }

        FormBorderStyle = savedBorderStyle;
        WindowState = savedWindowState;
        if (savedWindowState == FormWindowState.Normal)
        {
            Bounds = savedBounds;
        }

        contentLayout.ColumnStyles[0].Width = 190;
        contentLayout.ColumnStyles[2].Width = 300;
        rootLayout.RowStyles[0].Height = 64;
        rootLayout.RowStyles[2].Height = 30;
        headerPanel.Visible = true;
        footerPanel.Visible = true;
        navigationPanel.Visible = true;
        treePanel.Visible = true;
        isMonitorFullscreen = false;
    }
}
