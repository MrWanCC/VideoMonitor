using VideoMonitor.Core.Mock;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.ViewModels;

public sealed class DeviceManagementCrudTests
{
    [Fact]
    public void AddDevice_AddsDeviceAndDefaultChannelToCurrentGroup()
    {
        var viewModel = CreateViewModel("西401溜井");
        var before = viewModel.Devices.Count;
        viewModel.AddDeviceCommand.Execute(null);
        FillValidDraft(viewModel.EditDraft, "新增IPC", "192.168.17.20");

        viewModel.SaveDeviceCommand.Execute(null);

        Assert.Equal(before + 1, viewModel.Devices.Count);
        var added = viewModel.Devices.Single(device => device.Name == "新增IPC");
        Assert.Equal(viewModel.SelectedGroup!.Id, added.GroupId);
        Assert.Equal(1, Assert.Single(added.Channels).ChannelNo);
    }

    [Fact]
    public void EditDevice_SaveUpdatesOriginalObject()
    {
        var viewModel = CreateViewModel("西401溜井");
        var original = viewModel.Devices[0];
        viewModel.EditDeviceCommand.Execute(original);
        viewModel.EditDraft.Name = "已编辑设备";

        viewModel.SaveDeviceCommand.Execute(null);

        Assert.Equal("已编辑设备", original.Name);
    }

    [Fact]
    public void CancelEdit_DoesNotMutateOriginalObject()
    {
        var viewModel = CreateViewModel("西401溜井");
        var original = viewModel.Devices[0];
        var originalName = original.Name;
        viewModel.EditDeviceCommand.Execute(original);
        viewModel.EditDraft.Name = "未保存名称";

        viewModel.CancelEditCommand.Execute(null);

        Assert.Equal(originalName, original.Name);
    }

    [Fact]
    public void DeleteDevice_RemovesOnlyAfterConfirmation()
    {
        var viewModel = CreateViewModel("西401溜井");
        var device = viewModel.Devices[0];
        viewModel.DeleteDeviceCommand.Execute(device);
        Assert.Contains(device, viewModel.Devices);

        viewModel.ConfirmDialogCommand.Execute(null);

        Assert.DoesNotContain(device, viewModel.Devices);
    }

    [Fact]
    public void EditDevice_MoveGroup_RemovesItFromCurrentAndAddsItToTarget()
    {
        var viewModel = CreateViewModel("西401溜井");
        var device = viewModel.Devices[0];
        var target = viewModel.Groups.Single(group => group.Name == "西402溜井");
        viewModel.EditDeviceCommand.Execute(device);
        viewModel.EditDraft.GroupId = target.Id;

        viewModel.SaveDeviceCommand.Execute(null);

        Assert.DoesNotContain(device, viewModel.Devices);
        viewModel.SelectGroupCommand.Execute(target);
        Assert.Contains(device, viewModel.Devices);
    }

    private static DeviceManagementViewModel CreateViewModel(string selectedGroup)
    {
        var data = MockDeviceData.Create();
        var viewModel = new DeviceManagementViewModel(data.Groups, data.Devices);
        viewModel.SelectGroupCommand.Execute(
            viewModel.Groups.Single(group => group.Name == selectedGroup));
        return viewModel;
    }

    private static void FillValidDraft(
        DeviceEditDraftViewModel draft,
        string name,
        string ipAddress)
    {
        draft.Name = name;
        draft.IpAddress = ipAddress;
        draft.SdkPort = "8000";
        draft.RtspPort = "554";
        draft.Username = "admin";
        draft.Password = "secret";
        draft.ChannelNo = "1";
        draft.ChannelName = "通道1";
    }
}
