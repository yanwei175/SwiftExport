using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExportClient.Features.Inventory.Outbounds.Models;
using SwiftExportClient.Features.Inventory.Outbounds.ViewModels;
using SwiftExportClient.Features.Inventory.Outbounds.Views;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SwiftExport.Client.Wpf.Tests.Features;

/// <summary>验证出库操作细权限、原账号恢复及真实删除选择边界。</summary>
public sealed partial class OutboundManagerPersistenceTests
{
    /// <summary>缺少操作细权限或 View 时按钮与直接命令均不发送写入，原编辑保留。</summary>
    [TestMethod]
    [DataRow(PermissionKeys.InventoryOutboundCreate, false)]
    [DataRow(PermissionKeys.InventoryOutboundEdit, true)]
    [DataRow(PermissionKeys.InventoryOutboundDelete, true)]
    [DataRow(PermissionKeys.InventoryOutboundView, false)]
    public async Task Permissions_GuardDirectDraftCommands(string removed, bool persisted)
    {
        using var f = new Fixture(); var row = f.Add();
        if (persisted) { await f.Vm.SaveCommand.ExecuteAsync(null); row.Remark = "本地编辑"; }
        var writes = f.Service.Writes;
        f.User.SetCurrentUser(new() { UserId = "outbound-test", Permissions = OutboundTestUsers.Create().CurrentUser!.Permissions.Where(x => x != removed).ToList() });
        if (removed == PermissionKeys.InventoryOutboundDelete)
        { Assert.IsFalse(f.Vm.DeleteSelectedCommand.CanExecute(null)); await f.Vm.DeleteSelectedCommand.ExecuteAsync(null); }
        else
        { Assert.IsFalse(f.Vm.SaveCommand.CanExecute(null)); await f.Vm.SaveCommand.ExecuteAsync(null); Assert.IsTrue(f.Vm.HeaderReadOnly); }
        Assert.IsTrue(f.Vm.Headers.Items.Contains(row)); Assert.AreEqual(writes, f.Service.Writes);
    }

    /// <summary>混合保存先验全部权限；只创建权限仍能连续保存多张新单，不因首单变为已保存而误要求 Edit。</summary>
    [TestMethod]
    public async Task Save_PrechecksMixedPermissionsAndSupportsCreateOnlyBatch()
    {
        using var f = new Fixture(); var existing = f.Add(); await f.Vm.SaveCommand.ExecuteAsync(null);
        existing.Remark = "修改"; f.Add();
        f.User.SetCurrentUser(new() { UserId = "outbound-test", Permissions = [PermissionKeys.InventoryOutboundView, PermissionKeys.InventoryOutboundCreate] });
        await f.Vm.SaveCommand.ExecuteAsync(null); Assert.AreEqual(1, f.Service.Writes);
        using var onlyCreate = new Fixture(); onlyCreate.User.SetCurrentUser(f.User.CurrentUser!); onlyCreate.Add(); onlyCreate.Add();
        await onlyCreate.Vm.SaveCommand.ExecuteAsync(null); Assert.AreEqual(2, onlyCreate.Service.Writes); Assert.IsFalse(onlyCreate.Vm.HasUnsavedChanges);
    }

    /// <summary>原因或过账确认等待期间换账号，不接受旧确认、不发送旧用户意图。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Confirmation_RejectsAccountChange(bool posting)
    {
        using var f = new Fixture(); var row = f.Add();
        Task work;
        if (posting)
        { await f.Vm.SaveCommand.ExecuteAsync(null); f.PostDialogs.Wait = new(); work = f.Vm.PostCommand.ExecuteAsync(null); }
        else { f.IssueReasonDialogs.Wait = new(); work = f.Vm.SaveCommand.ExecuteAsync(null); }
        f.User.SetCurrentUser(new() { UserId = "other", Permissions = OutboundTestUsers.Create().CurrentUser!.Permissions });
        if (posting) f.PostDialogs.Wait!.SetResult(true); else f.IssueReasonDialogs.Wait!.SetResult();
        await work;
        Assert.AreEqual(0, f.Service.Posts.Count);
        Assert.AreEqual(posting ? 1 : 0, f.Service.Creates.Count);
        if (!posting) Assert.IsTrue(string.IsNullOrEmpty(row.IssueReason));
    }

    /// <summary>所有原命令未知后均冻结账号和负载；换账号或撤权不能重放，原账号恢复后只产生一次业务事实。</summary>
    [TestMethod]
    [DataRow("create")]
    [DataRow("update")]
    [DataRow("delete")]
    [DataRow("post")]
    [DataRow("reverse")]
    public async Task UnknownWrite_RestoresOriginalAccountAndPayload(string operation)
    {
        using var f = new Fixture(); var row = f.Add();
        if (operation != "create") await f.Vm.SaveCommand.ExecuteAsync(null);
        if (operation == "reverse") await f.Vm.PostCommand.ExecuteAsync(null);
        if (operation == "update") row.Remark = "修改意图";
        if (operation == "post") f.Service.LosePostResponse = true;
        else if (operation == "reverse") f.Service.LoseReverseResponse = true;
        else f.Service.LoseNextResponse = true;
        var command = operation switch { "delete" => f.Vm.DeleteSelectedCommand, "post" => f.Vm.PostCommand, "reverse" => f.Vm.ReverseCommand, _ => f.Vm.SaveCommand };
        await command.ExecuteAsync(null); Assert.IsTrue(row.IsPersistencePending);
        object Original() => operation switch { "create" => f.Service.Creates.Last(), "update" => f.Service.Updates.Last(),
            "delete" => f.Service.Deletes.Last(), "post" => f.Service.Posts.Last(), _ => f.Service.Reverses.Last() };
        var original = Original(); var json = JsonSerializer.Serialize(original);
        f.User.SetCurrentUser(new() { UserId = "other", Permissions = OutboundTestUsers.Create().CurrentUser!.Permissions });
        Assert.IsFalse(f.Vm.RefreshCommand.CanExecute(null)); await f.Vm.RefreshCommand.ExecuteAsync(null);
        f.User.SetCurrentUser(new() { UserId = "outbound-test", Permissions = [PermissionKeys.InventoryOutboundView] });
        Assert.IsFalse(f.Vm.RefreshCommand.CanExecute(null)); await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.IsTrue(row.IsPersistencePending); Assert.IsFalse(await f.Vm.CanCloseAsync(CancellationToken.None));
        f.User.SetCurrentUser(OutboundTestUsers.Create().CurrentUser!); await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreSame(original, Original()); Assert.AreEqual(json, JsonSerializer.Serialize(Original())); Assert.IsFalse(row.IsPersistencePending);
        Assert.AreEqual(operation == "post" || operation == "reverse" ? 1 : operation == "create" ? 1 : 2, f.Service.Writes);
    }

    /// <summary>写成功后晚到响应只保留原回执，不覆盖新账号反馈；原账号恢复仅 View 权限即可读取。</summary>
    [TestMethod]
    public async Task LateSuccess_AfterAccountChangeDoesNotPolluteFeedbackOrRepeatCreate()
    {
        using var f = new Fixture(); var row = f.Add(); row.IssueReason = "已填原因"; f.Service.Wait = new();
        var work = f.Vm.SaveCommand.ExecuteAsync(null);
        f.User.SetCurrentUser(new() { UserId = "other", Permissions = OutboundTestUsers.Create().CurrentUser!.Permissions });
        f.Vm.ResultSummary = "新账号反馈"; f.Service.Wait.SetResult(); await work;
        Assert.AreEqual("新账号反馈", f.Vm.ResultSummary);
        f.User.SetCurrentUser(new() { UserId = "outbound-test", Permissions = [PermissionKeys.InventoryOutboundView] });
        await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual(1, f.Service.Creates.Count); Assert.IsFalse(row.IsPersistencePending);
    }

    /// <summary>原过账已成功后可以接纳更高版本的反向详情，View 只读恢复不重新过账。</summary>
    [TestMethod]
    public async Task AcceptedPost_AdoptsLaterReversalWithoutRepeatingWrite()
    {
        using var f = new Fixture(); var row = f.Add(); await f.Vm.SaveCommand.ExecuteAsync(null);
        f.Service.FailDetails = true; await f.Vm.PostCommand.ExecuteAsync(null); Assert.IsTrue(row.IsPersistencePending);
        f.Service.FailDetails = false; var detail = (await f.Service.GetAsync(row.Uid, CancellationToken.None)).Data;
        detail.Header.Version++; detail.Header.PostingStatus = OutboundPostingStatusEnum.Reversed;
        f.User.SetCurrentUser(new() { UserId = "outbound-test", Permissions = [PermissionKeys.InventoryOutboundView] });
        await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.IsFalse(row.IsPersistencePending); Assert.AreEqual(OutboundPostingStatusEnum.Reversed, row.PostingStatus);
        Assert.AreEqual(1, f.Service.Posts.Count); StringAssert.Contains(f.Vm.ResultSummary, "随后已变化");
    }

    /// <summary>清空已保存明细必须先保存空集合，禁用时仍显示下一步提示；保存后才允许换仓。</summary>
    [TestMethod]
    public async Task WarehouseChange_RequiresSavingRemovedPersistedLines()
    {
        using var f = new Fixture(); var row = f.Add(); await f.Vm.SaveCommand.ExecuteAsync(null);
        row.Lines.SelectedItem = row.Lines.Items.Single(); f.Vm.DeleteSelectedLineCommand.Execute(null);
        Assert.IsFalse(f.Vm.OpenWarehousePickerCommand.CanExecute(row)); StringAssert.Contains(f.Vm.ResultSummary, "先保存空明细");
        var warehouse = row.WarehouseSiteId; await f.Vm.OpenWarehousePickerCommand.ExecuteAsync(row); Assert.AreEqual(warehouse, row.WarehouseSiteId);
        await f.Vm.SaveCommand.ExecuteAsync(null); Assert.IsTrue(f.Vm.OpenWarehousePickerCommand.CanExecute(row)); Assert.AreEqual(0, row.Lines.DeletedItems.Count);
    }

    /// <summary>删除未选中行保留存活选择；未知删除恢复也不跳走，且保持同一 CommandId。</summary>
    [TestMethod]
    public async Task DeleteRecovery_PreservesSurvivingSelection()
    {
        using var f = new Fixture(); var removed = f.Add(); var selected = f.Add(); await f.Vm.SaveCommand.ExecuteAsync(null);
        f.Vm.SelectedItem = selected; removed.IsChecked = true; f.Service.LoseNextResponse = true;
        await f.Vm.DeleteSelectedCommand.ExecuteAsync(null); Assert.AreSame(selected, f.Vm.SelectedItem);
        var command = f.Service.Deletes.Single(); await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreSame(selected, f.Vm.SelectedItem); Assert.IsFalse(f.Vm.Headers.Items.Contains(removed)); Assert.AreSame(command, f.Service.Deletes.Last());
    }

    /// <summary>真实头表删除覆盖首行、末行、唯一行及删除其他行时保留存活当前行。</summary>
    [TestMethod]
    [DataRow(3, 0, 0, 1)]
    [DataRow(3, 2, 2, 1)]
    [DataRow(1, 0, 0, -1)]
    [DataRow(3, 0, 2, 2)]
    public void View_DeleteHeaderPreservesSelectionOrChoosesNeighbor(int count, int deleting, int selected, int expected)
    {
        Controls.DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load(); using var f = new Fixture(); var rows = Enumerable.Range(0, count).Select(_ => f.Add()).ToArray();
            var view = new OutboundManagerView { DataContext = f.Vm };
            var window = TestWindow(view);
            try
            {
                window.Show(); f.Vm.SelectedItem = rows[selected]; rows[deleting].IsChecked = true; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                await f.Vm.DeleteSelectedCommand.ExecuteAsync(null); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var target = expected < 0 ? null : rows[expected]; Assert.AreSame(target, f.Vm.SelectedItem); Assert.AreSame(target, ((DataGrid)view.FindName("HeaderGrid")).SelectedItem);
            }
            finally { window.Close(); }
        });
    }

    /// <summary>真实排序批量删除按当前显示顺序寻找上一存活行，全删后安全清空。</summary>
    [TestMethod]
    public void View_DeleteSortedBatchAndLinesKeepsNativeSelection()
    {
        Controls.DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load(); using var f = new Fixture(); var first = f.Add(); var second = f.Add(); var third = f.Add();
            first.ShipmentDate = DateTime.Today; second.ShipmentDate = DateTime.Today.AddDays(1); third.ShipmentDate = DateTime.Today.AddDays(2);
            var view = new OutboundManagerView { DataContext = f.Vm }; var window = TestWindow(view);
            try
            {
                window.Show(); var grid = (DataGrid)view.FindName("HeaderGrid"); grid.Items.SortDescriptions.Add(new(nameof(OutboundHeaderModel.ShipmentDate), ListSortDirection.Descending));
                f.Vm.SelectedItem = second; second.IsChecked = true; first.IsChecked = true;
                await f.Vm.DeleteSelectedCommand.ExecuteAsync(null); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.AreSame(third, f.Vm.SelectedItem); Assert.AreSame(third, grid.SelectedItem);
                f.AddLine(third); var lines = third.Lines.Items.ToArray(); third.Lines.SelectedItem = lines[1]; lines[0].IsChecked = true;
                f.Vm.DeleteSelectedLineCommand.Execute(null); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.AreSame(lines[1], third.Lines.SelectedItem); Assert.AreSame(lines[1], ((DataGrid)view.FindName("LineGrid")).CurrentItem);
                third.Lines.Items[0].IsChecked = true; f.Vm.DeleteSelectedLineCommand.Execute(null); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.IsNull(third.Lines.SelectedItem); Assert.IsNull(((DataGrid)view.FindName("LineGrid")).CurrentItem);
                third.IsChecked = true; await f.Vm.DeleteSelectedCommand.ExecuteAsync(null); Assert.IsNull(f.Vm.SelectedItem);
            }
            finally { window.Close(); }
        });
    }

    /// <summary>部分删除失败保留当前仍存活的行，已成功删除不会重新出现。</summary>
    [TestMethod]
    public async Task DeletePartialFailure_PreservesLivingAnchor()
    {
        using var f = new Fixture(); var anchor = f.Add(); var first = f.Add(); var second = f.Add(); await f.Vm.SaveCommand.ExecuteAsync(null);
        f.Vm.SelectedItem = anchor; first.IsChecked = true; second.IsChecked = true; f.Service.FailDeleteAt = 2;
        await f.Vm.DeleteSelectedCommand.ExecuteAsync(null);
        Assert.AreSame(anchor, f.Vm.SelectedItem); Assert.IsFalse(f.Vm.Headers.Items.Contains(first)); Assert.IsTrue(f.Vm.Headers.Items.Contains(second));
    }

    /// <summary>实际日期模板、表格及直接删行都遵守 Busy、冲突、页面只读与权限门禁。</summary>
    [TestMethod]
    public void View_DateAndCommandsRespectBusyConflictAndReadOnly()
    {
        Controls.DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load(); using var f = new Fixture(); var row = f.Add(); row.IssueReason = "已填原因";
            var view = new OutboundManagerView { DataContext = f.Vm };
            var header = (DataGrid)view.FindName("HeaderGrid");
            var date = (DatePicker)((DataGridTemplateColumn)header.Columns[4]).CellTemplate.LoadContent(); date.DataContext = row;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Assert.IsTrue(date.IsEnabled);
            f.Service.Wait = new(); var saving = f.Vm.SaveCommand.ExecuteAsync(null);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Assert.IsFalse(date.IsEnabled); Assert.IsTrue(f.Vm.LinesReadOnly);
            f.Service.Wait.SetResult(); await saving;
            row.Remark = "编辑"; f.Service.ConflictUpdate = true; await f.Vm.SaveCommand.ExecuteAsync(null);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Assert.IsTrue(row.HasVersionConflict); Assert.IsFalse(date.IsEnabled);
            Assert.IsFalse(f.Vm.OpenOutboundItemDialogCommand.CanExecute(null));
            row.Lines.SelectedItem = row.Lines.Items.Single(); f.Vm.DeleteSelectedLineCommand.Execute(null); Assert.AreEqual(1, row.Lines.Items.Count);
            row.HasVersionConflict = false;
            typeof(SwiftExportClient.Mvvm.ViewModelBaseEx).GetProperty(nameof(f.Vm.IsReadOnly))!.SetValue(f.Vm, true);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Assert.IsFalse(date.IsEnabled); Assert.IsTrue(f.Vm.HeaderReadOnly);
            Assert.IsFalse(f.Vm.AddNewHeaderRowCommand.CanExecute(null)); Assert.IsFalse(f.Vm.SaveCommand.CanExecute(null));
        });
    }

    /// <summary>明细实际 CurrentItem 在被删后按上一行或后继定位，唯一行清空安全。</summary>
    [TestMethod]
    [DataRow(3, 0, 1)]
    [DataRow(3, 2, 1)]
    [DataRow(1, 0, -1)]
    public void View_DeleteSelectedLineChoosesVisibleNeighbor(int count, int deleting, int expected)
    {
        Controls.DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load(); using var f = new Fixture(); var row = f.Add();
            for (var i = 1; i < count; i++) f.AddLine(row);
            var lines = row.Lines.Items.ToArray(); var view = new OutboundManagerView { DataContext = f.Vm }; var window = TestWindow(view);
            try
            {
                window.Show(); row.Lines.SelectedItem = lines[deleting]; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                f.Vm.DeleteSelectedLineCommand.Execute(null); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var target = expected < 0 ? null : lines[expected]; Assert.AreSame(target, row.Lines.SelectedItem);
                Assert.AreSame(target, ((DataGrid)view.FindName("LineGrid")).CurrentItem);
            }
            finally { window.Close(); }
        });
    }

    /// <summary>建立不占任务栏的定向 WPF 测试窗口。</summary>
    private static Window TestWindow(object content) => new() { Content = content, Width = 1000, Height = 650, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
}
