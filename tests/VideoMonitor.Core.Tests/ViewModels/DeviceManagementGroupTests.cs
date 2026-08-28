using VideoMonitor.Core.Mock;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.ViewModels;

public sealed class DeviceManagementGroupTests
{
    [Fact]
    public void SelectGroup_FiltersDevicesToSelectedGroup()
    {
        var viewModel = CreateViewModel();
        var group = viewModel.Groups.Single(item => item.Name == "西401溜井");

        viewModel.SelectGroupCommand.Execute(group);

        Assert.Equal(3, viewModel.Devices.Count);
        Assert.All(viewModel.Devices, device => Assert.Equal(group.Id, device.GroupId));
    }

    [Theory]
    [InlineData("西401", 3)]
    [InlineData("192.168.17", 3)]
    [InlineData("192.168.17.6", 1)]
    public void SearchKeyword_FiltersCurrentGroupByNameOrIp(string keyword, int expected)
    {
        var viewModel = CreateViewModel("西401溜井");

        viewModel.SearchKeyword = keyword;

        Assert.Equal(expected, viewModel.Devices.Count);
    }

    [Fact]
    public void DeleteNonEmptyGroup_IsBlockedWithoutConfirmation()
    {
        var viewModel = CreateViewModel("西401溜井");
        var group = viewModel.SelectedGroup!;

        viewModel.DeleteGroupCommand.Execute(group);

        Assert.True(viewModel.IsDialogOpen);
        Assert.Equal(DeviceDialogMode.Information, viewModel.DialogMode);
        Assert.Equal("该分组下仍有设备，请先移动或删除设备。", viewModel.DialogMessage);
        Assert.Contains(group, viewModel.Groups);
    }

    [Fact]
    public void CancelRename_DoesNotChangeOriginalName()
    {
        var viewModel = CreateViewModel("西402溜井");
        var group = viewModel.SelectedGroup!;

        viewModel.BeginRenameGroupCommand.Execute(group);
        viewModel.EditingGroupName = "已修改但未保存";
        viewModel.CancelGroupEditCommand.Execute(null);

        Assert.Equal("西402溜井", group.Name);
    }

    [Fact]
    public void DeleteEmptyGroup_RequiresConfirmation()
    {
        var viewModel = CreateViewModel();
        var root = viewModel.Groups.Single(item => item.Name == "溜井监控");
        viewModel.BeginAddGroupCommand.Execute(root);
        viewModel.EditingGroupName = "临时空分组";
        viewModel.CommitGroupEditCommand.Execute(null);
        var group = viewModel.Groups.Single(item => item.Name == "临时空分组");

        viewModel.DeleteGroupCommand.Execute(group);

        Assert.Equal(DeviceDialogMode.Confirmation, viewModel.DialogMode);
        Assert.Contains(group, viewModel.Groups);
        viewModel.ConfirmDialogCommand.Execute(null);
        Assert.DoesNotContain(group, viewModel.Groups);
    }

    [Fact]
    public void AddGroup_CommitCreatesNamedChildUnderRoot()
    {
        var viewModel = CreateViewModel();
        var root = viewModel.Groups.Single(group => group.Name == "溜井监控");

        viewModel.BeginAddGroupCommand.Execute(root);
        viewModel.EditingGroupName = "东401溜井";
        viewModel.CommitGroupEditCommand.Execute(null);

        var added = viewModel.Groups.Single(group => group.Name == "东401溜井");
        Assert.Equal(root.Id, added.ParentId);
    }

    private static DeviceManagementViewModel CreateViewModel(string? selectedGroup = null)
    {
        var data = MockDeviceData.Create();
        var viewModel = new DeviceManagementViewModel(
            new InMemoryDeviceCatalog(data.Groups, data.Devices));
        if (selectedGroup is not null)
        {
            viewModel.SelectGroupCommand.Execute(
                viewModel.Groups.Single(group => group.Name == selectedGroup));
        }

        return viewModel;
    }
}
