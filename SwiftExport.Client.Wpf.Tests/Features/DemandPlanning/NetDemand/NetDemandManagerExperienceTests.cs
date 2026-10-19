using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Services;
using SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.UI;
using System.Reflection;

namespace SwiftExport.Client.Wpf.Tests.Features.DemandPlanning.NetDemand;

public sealed partial class NetDemandManagerSourceNavigationTests
{
    /// <summary>翻页和刷新沿用已应用快照，失败不改变页码、大小、行或条件集合。</summary>
    [TestMethod]
    public async Task AppliedQuery_PagingRefreshAndFailurePreserveFacts()
    {
        var api = new RecordingNetDemandApi(Guid.NewGuid());
        api.SearchHandler = (request, _) => Task.FromResult(Page(request, "accepted"));
        using var vm = CreateViewModel(api);
        vm.Keyword = "old"; vm.SkusText = "A\nB"; vm.SelectedSourceTypes.Add(vm.SourceTypeOptions[0]);
        await vm.InitializeAsync();
        vm.Keyword = "draft"; vm.SkusText = "C"; vm.SelectedSourceTypes.Clear();
        await vm.PageChangeCommand.ExecuteAsync(new() { NewIndex = 2, NewSize = 40 });
        Assert.AreEqual(2, vm.PageIndex); Assert.AreEqual(40, vm.PageSize);
        Assert.AreEqual("old", api.SearchRequests.Last().Keyword);
        CollectionAssert.AreEqual(new[] { "A", "B" }, api.SearchRequests.Last().Skus);
        CollectionAssert.AreEqual(new[] { "SO" }, api.SearchRequests.Last().SourceTypes);
        var row = vm.Items[0];
        api.SearchHandler = (_, _) => Task.FromResult(ServiceResult<PagedResponseBase<MaterialDemandManagerItemDto>>.Fail("读取失败"));
        await vm.PageChangeCommand.ExecuteAsync(new() { NewIndex = 3, NewSize = 80 });
        Assert.AreEqual(2, vm.PageIndex); Assert.AreEqual(40, vm.PageSize); Assert.AreSame(row, vm.Items[0]);
        await vm.RefreshCommand.ExecuteAsync(null); Assert.AreEqual("old", api.SearchRequests.Last().Keyword);
        var count = api.SearchRequests.Count;
        vm.DemandRequiredDateFrom = new(2027, 1, 1); vm.DemandRequiredDateTo = new(2026, 1, 1);
        await vm.SearchCommand.ExecuteAsync(null); Assert.AreEqual(count, api.SearchRequests.Count); Assert.AreSame(row, vm.Items[0]);
    }

    /// <summary>来源全量查询中和所有顶部限制，失败不切模式，返回恢复原普通快照。</summary>
    [TestMethod]
    public async Task SourceMode_FullFamilyIsIndependentAndCommitsOnlyOnSuccess()
    {
        var api = new RecordingNetDemandApi(Guid.NewGuid()); using var vm = CreateViewModel(api);
        vm.Keyword = "old"; vm.SkusText = "ONLY-PARENT"; vm.SelectedDemandRole = vm.DemandRoleOptions[1];
        await vm.InitializeAsync(); await vm.ToggleSourcePanelCommand.ExecuteAsync(null);
        var group = vm.SourceGroups.Single(); var row = vm.Items.Single();
        api.SearchHandler = (_, _) => Task.FromResult(ServiceResult<PagedResponseBase<MaterialDemandManagerItemDto>>.Fail("来源失败"));
        await vm.SelectSourceGroupCommand.ExecuteAsync(group); Assert.IsFalse(vm.IsSourceSelected); Assert.AreSame(row, vm.Items[0]); Assert.IsNull(vm.SelectedSourceGroup);
        api.SearchHandler = (request, _) => Task.FromResult(Page(request, "family", 301));
        await vm.SelectSourceGroupCommand.ExecuteAsync(group);
        var sent = api.SearchRequests.Last(); Assert.AreEqual(group.SourceUid, sent.SourceUid);
        Assert.AreEqual(string.Empty, sent.Keyword); Assert.AreEqual(0, sent.Skus.Count); Assert.IsNull(sent.IsCustomerSupplied);
        Assert.AreEqual("All", sent.QuickView); Assert.IsNull(sent.DemandRequiredDateFrom); Assert.AreEqual(0, sent.NetDemandIds.Count);
        Assert.AreEqual(301, vm.Items.Count); Assert.AreEqual(1, vm.TotalPages); Assert.IsFalse(vm.CanEditMainFilters);
        vm.Keyword = "unsent"; await vm.RefreshCommand.ExecuteAsync(null); Assert.AreEqual(group.SourceUid, api.SearchRequests.Last().SourceUid);
        await vm.ReturnToAllCommand.ExecuteAsync(null); Assert.IsFalse(vm.IsSourceSelected); Assert.AreEqual("old", api.SearchRequests.Last().Keyword);
    }

    /// <summary>来源从导航消失时，普通查询失败继续保留旧来源事实和模式。</summary>
    [TestMethod]
    public async Task SourceRemoval_FailedFallbackPreservesMode()
    {
        var api = new RecordingNetDemandApi(Guid.NewGuid()); using var vm = CreateViewModel(api);
        await vm.InitializeAsync(); await vm.ToggleSourcePanelCommand.ExecuteAsync(null); await vm.SelectSourceGroupCommand.ExecuteAsync(vm.SourceGroups.Single());
        var row = vm.Items[0]; var group = vm.SelectedSourceGroup;
        api.GroupHandler = (_, _) => Task.FromResult(ServiceResult<List<NetDemandSourceGroupDto>>.Ok([]));
        api.SearchHandler = (_, _) => Task.FromResult(ServiceResult<PagedResponseBase<MaterialDemandManagerItemDto>>.Fail("恢复失败"));
        await vm.RefreshCommand.ExecuteAsync(null); Assert.IsTrue(vm.IsSourceSelected); Assert.AreSame(group, vm.SelectedSourceGroup); Assert.AreSame(row, vm.Items[0]);
    }

    /// <summary>各刷新、左栏清空和建立入口均在丢弃本地建议前取得明确确认。</summary>
    [TestMethod]
    public async Task UnsavedSuggestion_AllReloadAndEstablishEntrypointsAreProtected()
    {
        var api = new RecordingNetDemandApi(Guid.NewGuid()); var dialogs = new ExperienceDialogs { Confirm = false }; var picker = new PickerStub(true);
        using var vm = CreateViewModel(api, dialog: dialogs, netDialog: picker);
        await vm.InitializeAsync(); await vm.ToggleSourcePanelCommand.ExecuteAsync(null);
        vm.Items[0].ApplySuggestedSupplier(new() { Uid = Guid.NewGuid(), SupplierCode = "S" });
        var reads = api.SearchRequests.Count; var groups = api.SourceGroupRequests.Count;
        await vm.RefreshCommand.ExecuteAsync(null); await vm.RefreshSourceGroupsCommand.ExecuteAsync(null); await vm.ClearSourceFiltersCommand.ExecuteAsync(null);
        await vm.EstablishFromSourcesCommand.ExecuteAsync(null); await vm.ReturnToAllCommand.ExecuteAsync(null);
        Assert.AreEqual(reads, api.SearchRequests.Count); Assert.AreEqual(groups, api.SourceGroupRequests.Count); Assert.AreEqual(0, picker.Calls);
        Assert.IsTrue(vm.HasUnsavedChanges); Assert.IsTrue(dialogs.ConfirmCalls >= 5);
        dialogs.Confirm = true; await vm.EstablishFromSourcesCommand.ExecuteAsync(null); Assert.AreEqual(1, picker.Calls); Assert.IsFalse(vm.HasUnsavedChanges);
    }

    /// <summary>回退采集真实原因并冻结整修订请求，未知和重试限流不得换身份或放开关闭。</summary>
    [TestMethod]
    public async Task Rollback_RealReasonAndUnknownRetryKeepOriginalRequest()
    {
        var api = new RecordingNetDemandApi(Guid.NewGuid()); var dialogs = new ExperienceDialogs { Reason = " " };
        using var vm = CreateViewModel(api, dialog: dialogs); await vm.InitializeAsync(); vm.Items[0].IsChecked = true;
        await vm.RollbackSourceEstablishmentCommand.ExecuteAsync(null); Assert.AreEqual(0, api.RollbackCommands.Count);
        dialogs.Reason = "BOM组成需要纠正"; api.RollbackOutcomes.Enqueue(ServiceErrorCodes.OutcomeUnknown); api.RollbackOutcomes.Enqueue(ServiceErrorCodes.RateLimited); api.RollbackOutcomes.Enqueue(null);
        await vm.RollbackSourceEstablishmentCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.HasPendingRollback); Assert.IsFalse(vm.CanEditSuggestion); Assert.IsFalse(await vm.CanCloseAsync(CancellationToken.None));
        var request = api.RollbackCommands[0]; Assert.AreEqual("BOM组成需要纠正", request.Reason); Assert.AreNotEqual(Guid.Empty, request.CommandId);
        Assert.AreEqual(vm.Items[0].GrossDemandId, request.Items.Single().GrossDemandId);
        StringAssert.Contains(dialogs.Scope, "整个物料修订"); StringAssert.Contains(dialogs.Scope, "1 个物料行");
        dialogs.Reason = "不能覆盖原输入"; vm.Items[0].IsChecked = false;
        await vm.RollbackSourceEstablishmentCommand.ExecuteAsync(null); Assert.IsTrue(vm.HasPendingRollback);
        await vm.RollbackSourceEstablishmentCommand.ExecuteAsync(null); Assert.IsFalse(vm.HasPendingRollback);
        Assert.IsTrue(api.RollbackCommands.All(x => ReferenceEquals(x, request))); Assert.AreEqual(2, dialogs.ReasonCalls);
    }

    /// <summary>业务阻断完整展示后端定位信息，释放未知门禁但保留事实和勾选，不解析消息。</summary>
    [TestMethod]
    public async Task Rollback_BusinessBlockerMessagePreservesRows()
    {
        var api = new RecordingNetDemandApi(Guid.NewGuid()); var dialogs = new ExperienceDialogs();
        using var vm = CreateViewModel(api, dialog: dialogs); await vm.InitializeAsync(); var row = vm.Items[0]; row.IsChecked = true;
        api.RollbackOutcomes.Enqueue("DownstreamExecutionExists"); await vm.RollbackSourceEstablishmentCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.HasPendingRollback); Assert.AreSame(row, vm.Items[0]); Assert.IsTrue(row.IsChecked);
        Assert.AreEqual(vm.ResultSummary, dialogs.Error); StringAssert.Contains(dialogs.Error, "SO-1001 / SKU-1");
    }

    /// <summary>建立与回退独立授权，模块可查看不等于可执行命令，采购敏感条件不发送。</summary>
    [TestMethod]
    public async Task Permissions_IndependentWritesPurchaseFiltersAndTracking()
    {
        var api = new RecordingNetDemandApi(Guid.NewGuid()); var user = MaterialUser();
        var nav = DispatchProxy.Create<INavigationService, MaterialNavigationRecorder>();
        using var vm = CreateViewModel(api, currentUser: user, navigation: nav); await vm.InitializeAsync(); vm.Items[0].IsChecked = true;
        Assert.IsFalse(vm.EstablishFromSourcesCommand.CanExecute(null)); Assert.IsFalse(vm.RollbackSourceEstablishmentCommand.CanExecute(null));
        await vm.RollbackSourceEstablishmentCommand.ExecuteAsync(null); Assert.AreEqual(0, api.RollbackCommands.Count); Assert.IsFalse(vm.OpenTraceCommand.CanExecute(null));
        vm.ActualSupplierKeyword = "hidden"; vm.PurchaseOrderNoFilter = "PO-hidden"; await vm.SearchCommand.ExecuteAsync(null);
        Assert.AreEqual(string.Empty, api.SearchRequests.Last().ActualPurchaseSupplierKeyword); Assert.AreEqual(string.Empty, api.SearchRequests.Last().PurchaseOrderNo);
        var navUser = MaterialUser(PermissionKeys.OrderTrackingView); using var allowed = CreateViewModel(api, currentUser: navUser, navigation: nav); await allowed.InitializeAsync();
        await allowed.OpenTraceCommand.ExecuteAsync(null); var call = ((MaterialNavigationRecorder)(object)nav).Calls.Single();
        Assert.AreEqual("material-tracking-detail", call.Route); Assert.AreEqual(allowed.SelectedItem!.Uid.ToString("N"), call.Key); Assert.AreEqual(allowed.SelectedItem.Uid, call.Parameter);
        Assert.IsFalse(new NavigationRouteRegistry().TryGetRoute("material-demand-trace", out _));
    }

    /// <summary>忽略取消的旧查询不能覆盖新结果，释放或会话替换后的来源响应不能回写。</summary>
    [TestMethod]
    public async Task Reads_LatestRequestAndSessionGuard()
    {
        var api = new RecordingNetDemandApi(Guid.NewGuid()); var old = new TaskCompletionSource<ServiceResult<PagedResponseBase<MaterialDemandManagerItemDto>>>();
        CancellationToken oldToken = default; api.SearchHandler = (_, token) => { oldToken = token; return old.Task; };
        var user = MaterialUser(); using var vm = CreateViewModel(api, currentUser: user);
        var first = vm.SearchCommand.ExecuteAsync(null);
        api.SearchHandler = (request, _) => Task.FromResult(Page(request, "new")); vm.Keyword = "new"; await vm.SearchCommand.ExecuteAsync(null);
        Assert.IsTrue(oldToken.IsCancellationRequested); old.SetResult(Page(new(), "old")); await first; Assert.AreEqual("new", vm.Items[0].Sku);
        var groups = new TaskCompletionSource<ServiceResult<List<NetDemandSourceGroupDto>>>(); api.GroupHandler = (_, _) => groups.Task;
        var sourceRead = vm.ToggleSourcePanelCommand.ExecuteAsync(null); user.SetCurrentUser(new MeDto { UserId = "other", Permissions = [PermissionKeys.DemandPlanningMaterialView] });
        groups.SetResult(ServiceResult<List<NetDemandSourceGroupDto>>.Ok([new() { SourceUid = Guid.NewGuid(), SourceNo = "old" }])); await sourceRead;
        Assert.AreEqual(0, vm.Items.Count); Assert.AreEqual(0, vm.SourceGroups.Count);
    }

    /// <summary>全选只修改当前可见行；隐藏建议仍被未保存检查发现。</summary>
    [TestMethod]
    public async Task VisibleSelection_ExcludesHiddenRowsButKeepsHiddenDrafts()
    {
        var source = Guid.NewGuid(); var gross = Guid.NewGuid(); var root = Guid.NewGuid();
        var api = new RecordingNetDemandApi(source) { SearchItems = [
            CreateManagerItem(source, gross, root, root, 1, false),
            CreateManagerItem(source, gross, root, Guid.NewGuid(), 2, true),
            CreateManagerItem(source, gross, root, Guid.NewGuid(), 3, true)] };
        using var vm = CreateViewModel(api); await vm.InitializeAsync(); vm.UpdateVisibleRows(vm.Items.Take(1)); vm.IsAllChecked = true;
        Assert.IsTrue(vm.Items[0].IsChecked); Assert.IsTrue(vm.Items.Skip(1).All(x => !x.IsChecked)); Assert.AreEqual(1, vm.CheckedCount);
        vm.Items[0].IsChecked = false; vm.Items[0].IsChecked = true; Assert.IsTrue(vm.Items.Skip(1).All(x => !x.IsChecked));
        vm.Items[2].ApplySuggestedSupplier(new() { Uid = Guid.NewGuid(), SupplierCode = "S" }); Assert.IsTrue(vm.HasUnsavedChanges);
    }

    private static ServiceResult<PagedResponseBase<MaterialDemandManagerItemDto>> Page(MaterialDemandManagerSearchRequest request, string sku, int count = 1)
        => ServiceResult<PagedResponseBase<MaterialDemandManagerItemDto>>.Ok(new()
        {
            Items = Enumerable.Range(0, count).Select(i => new MaterialDemandManagerItemDto
            {
                Uid = Guid.NewGuid(), GrossDemandId = Guid.NewGuid(), RootNetDemandId = Guid.NewGuid(), DecompositionRevision = 1,
                Version = 1, Sku = sku, SourceUid = request.SourceUid ?? Guid.NewGuid(), SourceType = "SO", SourceNo = "SO-1001", Unit = "PCS", NetQty = 1.234567m
            }).ToList(), TotalCount = count, PageIndex = request.PageIndex, PageSize = request.PageSize
        });

    private static CurrentUserService MaterialUser(params string[] permissions)
    {
        var user = new CurrentUserService(); user.SetCurrentUser(new MeDto { UserId = "u", Permissions = new[] { PermissionKeys.DemandPlanningMaterialView }.Concat(permissions).ToList() }); return user;
    }

    private sealed class ExperienceDialogs : IDialogService
    {
        public bool Confirm { get; set; } = true;
        public string Reason { get; set; } = "重新核对物料组成";
        public string Scope { get; private set; } = string.Empty;
        public string Error { get; private set; } = string.Empty;
        public int ConfirmCalls { get; private set; }
        public int ReasonCalls { get; private set; }
        public Task<DialogResult> ShowAsync(object content, DialogOptions options, IDialogContentState? state = null, CancellationToken cancellationToken = default)
        { var input = (MaterialDemandRollbackReasonViewModel)content; input.Reason = Reason; Scope = input.Scope; ReasonCalls++; return Task.FromResult(DialogResult.Primary); }
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default)
        { ConfirmCalls++; return Task.FromResult(Confirm); }
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) { Error = message; return Task.CompletedTask; }
    }

    private sealed class PickerStub(bool established) : INetDemandDialogService
    {
        public int Calls { get; private set; }
        public Task<bool> ShowSourceDemandCandidatesAsync(CancellationToken cancellationToken = default) { Calls++; return Task.FromResult(established); }
    }

    public class MaterialNavigationRecorder : DispatchProxy
    {
        public List<(string Route, string Key, object? Parameter)> Calls { get; } = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        { if (method?.Name == "OpenDocumentAsync") { Calls.Add(((string)args![0]!, (string)args[1]!, args[2])); return Task.CompletedTask; } throw new NotSupportedException(method?.Name); }
    }
}
