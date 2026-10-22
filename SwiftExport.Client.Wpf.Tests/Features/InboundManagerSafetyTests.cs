using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Inventory.Inbounds.Models;
using SwiftExportClient.Features.Inventory.Inbounds.Views;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SwiftExport.Client.Wpf.Tests.Features;

/// <summary>验证入库权限、成功回执恢复和真实模板的编辑保护。</summary>
public sealed partial class InboundManagerPersistenceTests
{
    /// <summary>撤销细权限后，按钮与直接命令均不能发送对应写入，查看权限也不可缺少。</summary>
    [TestMethod]
    [DataRow(PermissionKeys.InventoryInboundCreate, false)]
    [DataRow(PermissionKeys.InventoryInboundEdit, true)]
    [DataRow(PermissionKeys.InventoryInboundDelete, true)]
    [DataRow(PermissionKeys.InventoryInboundView, false)]
    public async Task DraftPermissions_GuardButtonsAndDirectExecution(string removed, bool persisted)
    {
        using var f = new Fixture();
        var row = f.Add();
        if (persisted) { await f.Vm.SaveCommand.ExecuteAsync(null); row.Remark = "尚未保存的输入"; }
        var before = f.Service.Writes;
        f.User.SetCurrentUser(new() { UserId = "inbound-test", Permissions = InboundTestUsers.Create().CurrentUser!.Permissions.Where(x => x != removed).ToList() });
        if (removed == PermissionKeys.InventoryInboundDelete)
        {
            Assert.IsFalse(f.Vm.DeleteSelectedCommand.CanExecute(null));
            await f.Vm.DeleteSelectedCommand.ExecuteAsync(null);
            Assert.IsTrue(f.Vm.Headers.Items.Contains(row));
        }
        else
        {
            Assert.IsFalse(f.Vm.SaveCommand.CanExecute(null));
            await f.Vm.SaveCommand.ExecuteAsync(null);
            Assert.IsTrue(f.Vm.HeaderReadOnly); Assert.IsTrue(f.Vm.LinesReadOnly);
        }
        Assert.AreEqual(before, f.Service.Writes);
    }

    /// <summary>混合保存缺少已有草稿编辑权限时，连有权限的新建请求也不得提前提交。</summary>
    [TestMethod]
    public async Task MixedSave_PrechecksEveryOperationPermission()
    {
        using var f = new Fixture();
        var existing = f.Add(); await f.Vm.SaveCommand.ExecuteAsync(null);
        existing.Remark = "修改"; var added = f.Add();
        f.User.SetCurrentUser(new() { UserId = "inbound-test", Permissions = [PermissionKeys.InventoryInboundView, PermissionKeys.InventoryInboundCreate] });
        Assert.IsFalse(f.Vm.SaveCommand.CanExecute(null)); await f.Vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(1, f.Service.Writes); Assert.IsTrue(added.IsNew); Assert.IsTrue(existing.IsDirty);
        f.User.SetCurrentUser(InboundTestUsers.Create().CurrentUser!);
        Assert.IsTrue(f.Vm.SaveCommand.CanExecute(null)); await f.Vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(3, f.Service.Writes); Assert.IsFalse(f.Vm.HasUnsavedChanges);
    }

    /// <summary>原过账成功后的更高版本反向详情可接纳，恢复只读查询不要求继续拥有过账权限。</summary>
    [TestMethod]
    public async Task AcceptedPost_AdoptsLaterReversalWithoutResending()
    {
        using var f = new Fixture();
        var row = f.Add(); await f.Vm.SaveCommand.ExecuteAsync(null);
        f.Service.FailDetails = true; await f.Vm.PostCommand.ExecuteAsync(null);
        Assert.IsTrue(row.IsPersistencePending); Assert.AreEqual(1, f.Service.Posts.Count);
        f.Service.FailDetails = false;
        var detail = (await f.Service.GetAsync(row.Uid, CancellationToken.None)).Data;
        detail.Header.Version++; detail.Header.PostingStatus = InboundPostingStatusEnum.Reversed;
        detail.Header.ReversedDate = DateTime.Today; detail.Header.ReverseReason = "其他用户已反向";
        f.Service.FailDetails = false;
        f.User.SetCurrentUser(new() { UserId = "inbound-test", Permissions = [PermissionKeys.InventoryInboundView] });
        Assert.IsTrue(f.Vm.RefreshCommand.CanExecute(null)); await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.IsFalse(row.IsPersistencePending); Assert.IsFalse(f.Vm.HasUnsavedChanges);
        Assert.AreEqual(InboundPostingStatusEnum.Reversed, row.PostingStatus);
        Assert.AreEqual(1, f.Service.Posts.Count); StringAssert.Contains(f.Vm.ResultSummary, "随后已变化");
        Assert.IsTrue(await f.Vm.CanCloseAsync(CancellationToken.None));
    }

    /// <summary>写入未知时切换账号或撤权不能重发；恢复原账号权限后复用原负载。</summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task UnknownDraft_RequiresOriginalUserAndPermission(bool switchAccount)
    {
        using var f = new Fixture(); var row = f.Add();
        f.Service.LoseNextResponse = true; await f.Vm.SaveCommand.ExecuteAsync(null);
        var original = f.Service.Creates.Single();
        f.User.SetCurrentUser(new() { UserId = switchAccount ? "different-user" : "inbound-test", Permissions = switchAccount
            ? InboundTestUsers.Create().CurrentUser!.Permissions : [PermissionKeys.InventoryInboundView] });
        Assert.IsFalse(f.Vm.RefreshCommand.CanExecute(null)); await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual(1, f.Service.Creates.Count); Assert.IsTrue(row.IsPersistencePending);
        Assert.IsFalse(await f.Vm.CanCloseAsync(CancellationToken.None));
        f.User.SetCurrentUser(InboundTestUsers.Create().CurrentUser!); await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreSame(original, f.Service.Creates[1]); Assert.AreEqual(1, f.Service.Writes); Assert.IsFalse(row.IsPersistencePending);
    }

    /// <summary>保存已经成功但详情不可读，保留成功回执并只重试 GET，不推断删除或重复新建。</summary>
    [TestMethod]
    public async Task AcceptedSave_UnavailableDetailKeepsReceiptWithoutRepeatingWrite()
    {
        using var f = new Fixture(); var row = f.Add(); f.Service.FailDetails = true;
        await f.Vm.SaveCommand.ExecuteAsync(null); await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual(1, f.Service.Creates.Count); Assert.IsTrue(row.IsPersistencePending);
        StringAssert.Contains(f.Vm.ResultSummary, "原命令已成功"); StringAssert.Contains(f.Vm.ResultSummary, "仅重试详情");
        f.Service.FailDetails = false; await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual(1, f.Service.Creates.Count); Assert.IsFalse(row.IsPersistencePending);
    }

    /// <summary>账号变化后的成功响应只保留原回执，不覆盖新会话反馈；原账号回来只读恢复。</summary>
    [TestMethod]
    public async Task WriteCompletion_AfterAccountChangeRetainsKnownSuccessWithoutOldFeedback()
    {
        using var f = new Fixture(); var row = f.Add(); f.Service.Wait = new();
        var save = f.Vm.SaveCommand.ExecuteAsync(null);
        f.User.SetCurrentUser(new() { UserId = "other", Permissions = InboundTestUsers.Create().CurrentUser!.Permissions });
        f.Vm.ResultSummary = "当前账号的反馈"; f.Service.Wait.SetResult(); await save;
        Assert.AreEqual("当前账号的反馈", f.Vm.ResultSummary); Assert.IsTrue(row.IsPersistencePending);
        f.User.SetCurrentUser(new() { UserId = "inbound-test", Permissions = [PermissionKeys.InventoryInboundView] });
        await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual(1, f.Service.Creates.Count); Assert.IsFalse(row.IsPersistencePending); Assert.IsFalse(row.IsNew);
    }

    /// <summary>实际日期模板响应权限和只读页面，类型固定展示，状态列消费当前行状态。</summary>
    [TestMethod]
    public void View_DateAndTypeRespectPermissionAndReadOnlyPage()
    {
        Controls.DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load(); using var f = new Fixture(); var row = f.Add();
            var view = new InboundManagerView { DataContext = f.Vm };
            var grid = (DataGrid)view.FindName("HeaderGrid");
            var date = (DatePicker)((DataGridTemplateColumn)grid.Columns.Single(x => Equals(x.Header, "收货日期"))).CellTemplate.LoadContent(); date.DataContext = row;
            var type = (DataGridTextColumn)grid.Columns.Single(x => Equals(x.Header, "入库类型"));
            var status = new TextBlock { DataContext = row };
            status.SetBinding(TextBlock.TextProperty, ((DataGridTextColumn)grid.Columns.Single(x => Equals(x.Header, "编辑状态"))).Binding);
            var lifecycle = new TextBlock { DataContext = row };
            lifecycle.SetBinding(TextBlock.TextProperty, ((DataGridTextColumn)grid.Columns.Single(x => Equals(x.Header, "单据状态"))).Binding);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.IsTrue(date.IsEnabled);
            Assert.IsTrue(type.IsReadOnly);
            Assert.AreEqual("新建草稿，可编辑", status.Text);
            Assert.AreEqual("活动", lifecycle.Text);
            f.User.SetCurrentUser(new() { UserId = "inbound-test", Permissions = [PermissionKeys.InventoryInboundView] });
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.IsFalse(date.IsEnabled); Assert.IsTrue(type.IsReadOnly); Assert.IsTrue(f.Vm.HeaderReadOnly);
            Assert.AreEqual("当前页面不可编辑", status.Text);
            f.User.SetCurrentUser(InboundTestUsers.Create().CurrentUser!);
            // 直接设置页面模式验证模板门禁；导航对脏草稿的保留由专门用例验证。
            typeof(SwiftExportClient.Mvvm.ViewModelBaseEx).GetProperty(nameof(f.Vm.IsReadOnly))!.SetValue(f.Vm, true);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.IsTrue(f.Vm.IsReadOnly); Assert.IsFalse(date.IsEnabled); Assert.IsTrue(f.Vm.LinesReadOnly);
        });
    }
}

/// <summary>验证查询边界、晚到响应和真实选择事件，不连接后端或数据库。</summary>
public sealed partial class InboundManagerQueryTests
{
    /// <summary>读取期间新选择替代旧读取，旧详情忽略取消也不能回写新选中单。</summary>
    [TestMethod]
    public async Task DetailSelection_LatestWinsWhileReading()
    {
        var api = new ApiStub(); using var vm = Create(api); await vm.SearchCommand.ExecuteAsync(null);
        var first = vm.SelectedItem!; first.IsDetailLoaded = false;
        var second = vm.Headers.Items[1];
        var pending = new TaskCompletionSource<ServiceResult<InboundManagerDetailDto>>();
        api.DetailHandler = uid => uid == first.Uid ? pending.Task : Task.FromResult(DetailFor(uid, "新选择"));
        var old = vm.LoadSelectedDetailCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.CanSelectHeaders); Assert.IsTrue(vm.HeaderReadOnly);
        vm.SelectedItem = second; await vm.LoadSelectedDetailCommand.ExecuteAsync(null);
        Assert.IsTrue(second.IsDetailLoaded); Assert.AreEqual("新选择", second.Remark);
        pending.SetResult(DetailFor(first.Uid, "过期")); await old;
        Assert.AreSame(second, vm.SelectedItem); Assert.IsFalse(first.IsDetailLoaded);
    }

    /// <summary>等待响应期间新增业务输入不被成功列表覆盖，旧行、页码和输入一起保留。</summary>
    [TestMethod]
    public async Task Query_DoesNotOverwriteInputCreatedInFlight()
    {
        var api = new ApiStub(); using var vm = Create(api); await vm.SearchCommand.ExecuteAsync(null);
        var row = vm.SelectedItem!; api.Pending = new(); var search = vm.SearchCommand.ExecuteAsync(null);
        row.Remark = "查询期间的输入";
        api.Pending.SetResult(ServiceResult<PagedResponseBase<InboundManagerListItemDto>>.Ok(new() { Items = [], TotalCount = 0 }));
        await search;
        Assert.AreSame(row, vm.SelectedItem); Assert.AreEqual("查询期间的输入", row.Remark);
        Assert.AreEqual(100, vm.TotalCount); StringAssert.Contains(vm.ResultSummary, "未覆盖");
    }

    /// <summary>真实 View 的列表回填只发一次详情；用户随后切换立即加载当前选择。</summary>
    [TestMethod]
    public void View_SelectionEventsDoNotDuplicateListDetail()
    {
        Controls.DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load(); var api = new ApiStub(); using var vm = Create(api);
            var view = new InboundManagerView { DataContext = vm };
            var window = new Window { Content = view, Width = 900, Height = 650, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            try
            {
                window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.AreEqual(1, api.DetailCalls);
                var second = vm.Headers.Items[1];
                ((DataGrid)view.FindName("HeaderGrid")).SelectedItem = second;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.AreSame(second, vm.SelectedItem); Assert.IsTrue(second.IsDetailLoaded); Assert.AreEqual(2, api.DetailCalls);
            }
            finally { window.Close(); }
        });
    }

    /// <summary>管理查询 200 字可提交，201 字按钮及直接入口阻断；纠正旧错误不覆盖后来的结果。</summary>
    [TestMethod]
    public async Task SearchLimits_RejectDirectInvalidInputAndPreserveNewerFeedback()
    {
        var api = new ApiStub(); using var vm = Create(api);
        vm.Keyword = new string('字', 200); Assert.IsTrue(vm.SearchCommand.CanExecute(null)); await vm.SearchCommand.ExecuteAsync(null);
        var row = vm.SelectedItem; vm.Keyword = new string('字', 201);
        Assert.IsFalse(vm.SearchCommand.CanExecute(null)); await vm.SearchCommand.ExecuteAsync(null);
        Assert.AreEqual(1, api.Requests.Count); Assert.AreSame(row, vm.SelectedItem);
        await vm.PageChangeCommand.ExecuteAsync(new() { NewIndex = 2, NewSize = 50 }); Assert.AreEqual(2, api.Requests.Count);
        Assert.AreEqual(200, api.Requests.Last().Keyword!.Length);
        vm.ResultSummary = "后来出现的业务反馈"; vm.Keyword = "修正"; Assert.AreEqual("后来出现的业务反馈", vm.ResultSummary);
        await vm.PageChangeCommand.ExecuteAsync(new() { NewIndex = 3, NewSize = 201 }); Assert.AreEqual(2, api.Requests.Count);
    }

    /// <summary>入库专用候选接口取完超过 200 条的数据，原选择器在完整会话快照上分页。</summary>
    [TestMethod]
    public async Task WarehouseOptions_UseInboundApiAndLoadAllPages()
    {
        var api = new ApiStub(); var requests = new List<PagedRequestBase>();
        var options = Enumerable.Range(1, 201).Select(i => new SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker.WarehouseSitePickerDto
            { Uid = Guid.NewGuid(), IsActive = true, Name = $"仓库{i}" }).ToList();
        api.OptionsHandler = request =>
        {
            requests.Add(request);
            return Task.FromResult(ServiceResult<PagedResponseBase<SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker.WarehouseSitePickerDto>>.Ok(new()
                { Items = options.Skip((request.PageIndex - 1) * request.PageSize).Take(request.PageSize).ToList(), TotalCount = 201,
                    PageIndex = request.PageIndex, PageSize = request.PageSize }));
        };
        var service = new SwiftExportClient.Features.Inventory.Inbounds.Services.InboundWarehousePickerService(api, new RunnerStub(), new DialogStub());
        using var picker = new SwiftExportClient.Features.Inventory.WarehouseSites.ViewModels.WarehousePickerViewModel(service.LoadOptionsAsync);
        await picker.OpenAsync(); Assert.AreEqual(201, picker.TotalCount);
        CollectionAssert.AreEqual(new[] { 1, 2 }, requests.Select(x => x.PageIndex).ToArray());
        Assert.IsTrue(requests.All(x => x.PageSize == InboundQueryLimits.MaxPageSize));
        picker.PageChangeCommand.Execute(new SwiftExportClient.Resources.Controls.PageChangeRequest { NewIndex = 2, NewSize = 200 });
        Assert.AreEqual(options.Last().Uid, picker.Items.Single().Uid);
        Assert.AreEqual(0, api.Requests.Count);
    }

    /// <summary>构造带有效版本及归属的独立详情响应。</summary>
    private static ServiceResult<InboundManagerDetailDto> DetailFor(Guid uid, string remark) => ServiceResult<InboundManagerDetailDto>.Ok(new()
    { Header = new() { Uid = uid, Version = 3, Remark = remark }, Lines = [] });
}

/// <summary>验证选源查询上限与修正提示所有权。</summary>
public sealed partial class InboundSourcePickerTests
{
    /// <summary>边界验证不清空已有结果，翻页保留已应用条件，纠错不覆盖新的 API 失败反馈。</summary>
    [TestMethod]
    public async Task SourceLimits_BlockInvalidSearchAndKeepAppliedFilters()
    {
        var api = new SourceApiStub();
        using var vm = new SwiftExportClient.Features.Inventory.Inbounds.ViewModels.InboundSourcePickerViewModel(api, new RunnerStub());
        vm.Keyword = new string('字', 200); await vm.SearchCommand.ExecuteAsync(null);
        var row = vm.Items.Single(); vm.Keyword = new string('字', 201);
        Assert.IsFalse(vm.SearchCommand.CanExecute(null)); await vm.SearchCommand.ExecuteAsync(null);
        Assert.AreEqual(1, api.Requests.Count); Assert.AreSame(row, vm.Items.Single());
        api.Pending = new(); var page = vm.PageChangeCommand.ExecuteAsync(2);
        Assert.AreEqual(200, api.Requests.Last().Keyword!.Length);
        api.Pending.SetResult(ServiceResult<PagedResponseBase<SwiftExport.Contracts.DTOs.Inventory.Inbounds.Sources.InboundSourceCandidateDto>>.Fail("新的接口反馈"));
        await page; vm.Keyword = "修正"; Assert.AreEqual("新的接口反馈", vm.StatusText);
        await vm.PageChangeCommand.ExecuteAsync(0); Assert.AreEqual(2, api.Requests.Count);
    }
}
