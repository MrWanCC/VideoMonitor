using VideoMonitor.Client.Models;

namespace VideoMonitor.Client.Controls;

public sealed class MonitorTreeControl : UserControl
{
    private readonly TreeView tree = new();

    public MonitorTreeControl(IReadOnlyList<MonitorGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        BackColor = Color.FromArgb(14, 24, 39);
        Dock = DockStyle.Fill;
        Padding = new Padding(12);

        tree.BackColor = BackColor;
        tree.ForeColor = Color.FromArgb(211, 222, 237);
        tree.BorderStyle = BorderStyle.None;
        tree.Dock = DockStyle.Fill;
        tree.Font = new Font("Microsoft YaHei UI", 10f);
        tree.FullRowSelect = true;
        tree.HideSelection = false;
        tree.Indent = 20;
        tree.ItemHeight = 32;
        tree.LineColor = Color.FromArgb(53, 86, 125);
        tree.ShowLines = true;
        tree.ShowPlusMinus = true;
        tree.AfterSelect += OnAfterSelect;

        AddCategory("卸矿站监控", MonitorGroupType.UnloadingStation, groups);
        AddCategory("溜井监控", MonitorGroupType.Shaft, groups);
        AddCategory("巷道监控", MonitorGroupType.Tunnel, groups);
        tree.ExpandAll();

        Controls.Add(tree);
    }

    public event EventHandler<MonitorGroup>? GroupSelected;

    private void AddCategory(
        string title,
        MonitorGroupType type,
        IEnumerable<MonitorGroup> groups)
    {
        var category = new TreeNode(title)
        {
            ForeColor = Color.FromArgb(106, 169, 255),
            NodeFont = new Font(tree.Font, FontStyle.Bold)
        };

        foreach (var group in groups.Where(group => group.Type == type))
        {
            category.Nodes.Add(new TreeNode(group.Name) { Tag = group });
        }

        tree.Nodes.Add(category);
    }

    private void OnAfterSelect(object? sender, TreeViewEventArgs e)
    {
        if (e.Node?.Tag is MonitorGroup group)
        {
            GroupSelected?.Invoke(this, group);
        }
    }
}
