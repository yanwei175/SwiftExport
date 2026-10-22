using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.MasterData;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.Text.Json;
using System.Net.Http;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Services;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement.PurchaseOrders;

/// <summary>将原预览门禁覆盖迁移为直接创建、输入恢复、结果核实与安全追加协议。</summary>
[TestClass]
public sealed partial class PurchaseDraftWorkflowViewModelTests
{
    /// <summary>实际准备视图持续显示来源、错误码和原因，关闭短暂消息不会丢失诊断。</summary>
    [TestMethod]
    public void PreparationView_DisplaysPersistentCreateFailure() => Controls.DatePickerAssistTests.RunWithApplication(async () =>
    {
        TestInfrastructure.ApplicationResourceLoader.Load();
        var api = new DraftApi { Handler = _ => Task.FromResult(ServiceResult<CreatePurchaseDraftsResult>.Fail("CAPACITY_CHANGED", "可创建数量已变化。")) };
        using var vm = Create(api); using var picker = new PurchaseCandidateSelectionViewModel(api, new DirectApiCallRunner());
        picker.AttachWorkflow(vm); await Open(vm, Row());
        var view = new SwiftExportClient.Features.Procurement.PurchaseOrders.Views.PurchasePreparationView { DataContext = picker };
        var window = new System.Windows.Window { Content = view, Width = 1400, Height = 850, Left = -5000, Top = -5000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show(); await vm.CreateCommand.ExecuteAsync(null); view.UpdateLayout();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var texts = Descendants(view).OfType<System.Windows.Controls.TextBlock>().Select(x => x.Text).ToArray();
            Assert.IsTrue(texts.Any(x => x.Contains("CAPACITY_CHANGED") && x.Contains("可创建数量已变化") && x.Contains(vm.Items.Single().SourceNo)));
        }
        finally { window.Close(); }
    });

    /// <summary>创建后直接进入结果页，返回准备页沿用同一会话；不再存在独立审核阶段。</summary>
    [TestMethod]
    public void Container_GoesDirectlyFromPreparationToResult() => TestInfrastructure.StaTest.RunAsync(async () =>
    {
        var api = new DraftApi(); using var vm = Create(api); using var picker = new PurchaseCandidateSelectionViewModel(api, new DirectApiCallRunner());
        using var container = new PurchaseOrderFromMaterialViewModel(picker, vm);
        await container.OpenAsync(); await vm.UpdateSelectionAsync(new() { Rows = [Row()] }); await vm.CreateCommand.ExecuteAsync(null);
        Assert.AreEqual(PurchaseOrderFromMaterialStage.Result, container.Stage);
        var result = (PurchaseDraftResultViewModel)container.CurrentPage; result.ReturnToPreparationCommand.Execute(null);
        Assert.AreEqual(PurchaseOrderFromMaterialStage.Selection, container.Stage); Assert.AreSame(picker, container.CurrentPage);
        Assert.AreEqual(1, vm.Results.Count);
    });

    private static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i); yield return child; foreach (var item in Descendants(child)) yield return item; }
    }
    /// <summary>缺少供应商产品关系不再依赖预览阻断；创建仅发送用户输入及需求并发身份。</summary>
    [TestMethod]
    public async Task DirectCreate_SendsVersionAndInputsWithoutPreviewOrProductSnapshot()
    {
        var api = new DraftApi(); using var vm = Create(api); var row = Row();
        await Open(vm, row); await vm.CreateCommand.ExecuteAsync(null);
        var command = api.Commands.Single(); var item = command.Sources.Single().Items.Single();
        Assert.AreEqual(row.Uid, item.NetDemandId); Assert.AreEqual(row.Version, item.ExpectedVersion);
        Assert.AreEqual(3m, item.PurchaseQty); Assert.AreEqual(row.SupplierId, item.SupplierId);
        Assert.AreEqual(7m, item.UnitPrice); Assert.AreEqual(0, api.PreviewCalls);
        var json = JsonSerializer.Serialize(command);
        Assert.IsFalse(json.Contains("PreviewToken")); Assert.IsFalse(json.Contains("ExpectedRemainingQty"));
        Assert.IsFalse(json.Contains("Sku")); Assert.IsTrue(vm.HasAppliedResults); Assert.AreEqual(0, vm.Items.Count);
        Assert.IsTrue(vm.CanClose); Assert.AreEqual(1, vm.Results.Single().PurchaseDrafts.Count);
    }

    /// <summary>明确业务拒绝保留所有输入与精确来源、错误码和原因，修正后才能新提交。</summary>
    [TestMethod]
    public async Task DefinitiveFailure_RetainsInputsAndPersistentDiagnostics()
    {
        var api = new DraftApi { Handler = _ => Task.FromResult(ServiceResult<CreatePurchaseDraftsResult>.Fail("CAPACITY_CHANGED", "当前可创建数量仅为 2。")) };
        using var vm = Create(api); var row = Row(); await Open(vm, row); var id = vm.ExecutionRequestId;
        await vm.CreateCommand.ExecuteAsync(null);
        StringAssert.Contains(vm.ResultSummary, row.SourceNo); StringAssert.Contains(vm.ResultSummary, "CAPACITY_CHANGED");
        StringAssert.Contains(vm.ResultSummary, "当前可创建数量仅为 2"); Assert.AreEqual(3m, vm.Items.Single().PurchaseQty);
        Assert.IsFalse(vm.HasUnknownCreateResult); Assert.IsTrue(vm.CanClose); Assert.IsTrue(vm.IsStale);
        Assert.AreNotEqual(id, vm.ExecutionRequestId); Assert.IsFalse(vm.CreateCommand.CanExecute(null));
        row.PurchaseQty = 2; await vm.UpdateSelectionAsync(new() { Rows = [row] });
        Assert.IsTrue(vm.CreateCommand.CanExecute(null));
    }

    /// <summary>逐来源部分成功只排除已成功来源，失败来源的数量、价格、交期仍可恢复。</summary>
    [TestMethod]
    public async Task PartialSuccess_KeepsFailureInputsAndExcludesSuccessfulSourceFromNextRequest()
    {
        var api = new DraftApi(); using var vm = Create(api); var a = Row(); var b = Row();
        api.Handler = command => { var response = Success(command); response.Data.Sources[1].Outcome = NetDemandCommandOutcomeEnum.Failed;
            response.Data.Sources[1].ErrorCode = "SUPPLIER_CHANGED"; response.Data.Sources[1].Message = "供应商已停用。";
            response.Data.Sources[1].PurchaseDrafts.Clear(); return Task.FromResult(response); };
        await Open(vm, a, b); var completed = 0; vm.CreationCompleted += (_, _) => completed++;
        await vm.CreateCommand.ExecuteAsync(null);
        Assert.AreEqual(1, completed); Assert.IsTrue(vm.IsSourceCompleted(a.SourceType, a.SourceUid));
        Assert.AreEqual(b.Uid, vm.Items.Single().NetDemandId); Assert.AreEqual(b.PlannedDeliveryDate, vm.Items.Single().PlannedDeliveryDate);
        StringAssert.Contains(vm.ResultSummary, "SUPPLIER_CHANGED");
        await vm.UpdateSelectionAsync(new() { Rows = [a, b] }); api.Handler = command => Task.FromResult(Success(command));
        await vm.CreateCommand.ExecuteAsync(null);
        Assert.AreEqual(b.SourceUid, api.Commands.Last().Sources.Single().SourceUid); Assert.AreEqual(2, vm.Results.Count);
    }

    /// <summary>返回准备页时只清除已成功来源选择，失败行保留输入及可定位错误。</summary>
    [TestMethod]
    public void PartialSuccess_UpdatesOnlySuccessfulCandidateSelection() => TestInfrastructure.StaTest.RunAsync(async () =>
    {
        var api = new DraftApi(); using var vm = Create(api); using var picker = new PurchaseCandidateSelectionViewModel(api, new DirectApiCallRunner());
        picker.AttachWorkflow(vm); var a = Row(); var b = Row(); a.IsChecked = true; b.IsChecked = true; picker.Items.Add(a); picker.Items.Add(b);
        api.Handler = command => { var response = Success(command); response.Data.Sources[1].Outcome = NetDemandCommandOutcomeEnum.Failed;
            response.Data.Sources[1].ErrorCode = "SUPPLIER_CHANGED"; response.Data.Sources[1].Message = "供应商已停用。";
            response.Data.Sources[1].PurchaseDrafts.Clear(); return Task.FromResult(response); };
        await Open(vm, a, b); await vm.CreateCommand.ExecuteAsync(null);
        Assert.IsFalse(a.IsChecked); Assert.IsTrue(b.IsChecked); Assert.AreEqual(3m, b.PurchaseQty); Assert.AreEqual(7m, b.UnitPrice);
        StringAssert.Contains(b.ValidationMessage, "SUPPLIER_CHANGED"); StringAssert.Contains(b.ValidationMessage, "供应商已停用");
    });

    /// <summary>未知时原身份和负载被冻结，保存后可关闭；重开仍只重试原提交。</summary>
    [TestMethod]
    [DataRow(ServiceErrorCodes.OutcomeUnknown)]
    [DataRow(ServiceErrorCodes.Unexpected)]
    [DataRow(ServiceErrorCodes.CommandBusy)]
    [DataRow(PurchaseDraftErrorCodes.IdempotencyPartialState)]
    public async Task UnknownFailure_RetriesIdenticalPayloadAndRejectsNewSession(string code)
    {
        var api = new DraftApi { Handler = _ => Task.FromResult(ServiceResult<CreatePurchaseDraftsResult>.Fail(code, "待核实")) };
        using var vm = Create(api); await Open(vm, Row()); var id = vm.ExecutionRequestId;
        await vm.CreateCommand.ExecuteAsync(null); Assert.IsTrue(vm.HasUnknownCreateResult); Assert.IsTrue(vm.CanClose);
        Assert.ThrowsExactly<InvalidOperationException>(() => vm.BeginSession()); vm.Close(); Assert.AreEqual(1, vm.Items.Count);
        vm.OpenSession(); Assert.AreEqual(id, vm.ExecutionRequestId);
        var original = JsonSerializer.Serialize(api.Commands.Single()); vm.Items.Single().PurchaseQty = 99;
        api.Handler = command => Task.FromResult(Success(command)); await vm.CreateCommand.ExecuteAsync(null);
        Assert.AreEqual(original, JsonSerializer.Serialize(api.Commands.Last())); Assert.AreEqual(id, api.Commands.Last().ExecutionRequestId);
        Assert.IsFalse(vm.HasUnknownCreateResult); Assert.IsTrue(vm.CanClose);
    }

    /// <summary>成功来源与未知来源混合时仍重放完整原请求，不能拆成新身份或丢弃已成功部分。</summary>
    [TestMethod]
    public async Task PartialUnknown_KeepsWholeOriginalRequestUntilEverySourceIsDefinite()
    {
        var api = new DraftApi(); api.Handler = command => { var result = Success(command);
            result.Data.Sources[1].Outcome = NetDemandCommandOutcomeEnum.Failed; result.Data.Sources[1].ErrorCode = PurchaseDraftErrorCodes.IdempotencyPartialState;
            result.Data.Sources[1].PurchaseDrafts.Clear(); return Task.FromResult(result); };
        using var vm = Create(api); await Open(vm, Row(), Row()); await vm.CreateCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.HasUnknownCreateResult); Assert.AreEqual(2, vm.Items.Count); Assert.IsTrue(vm.HasAppliedResults);
        var original = JsonSerializer.Serialize(api.Commands.Single()); api.Handler = c => Task.FromResult(Success(c));
        await vm.CreateCommand.ExecuteAsync(null); Assert.AreEqual(original, JsonSerializer.Serialize(api.Commands.Last()));
        Assert.AreEqual(2, vm.Results.Count); Assert.AreEqual(0, vm.Items.Count);
    }

    /// <summary>身份、结果枚举和成功引用必须完整，否则视为未知结果保留原请求。</summary>
    [TestMethod]
    [DataRow("identity")][DataRow("outcome")][DataRow("draft")][DataRow("source")]
    public async Task MalformedSuccess_RemainsUnknown(string defect)
    {
        var api = new DraftApi(); api.Handler = command => { var result = Success(command);
            switch (defect) { case "identity": result.Data.ExecutionRequestId = Guid.NewGuid(); break;
                case "outcome": result.Data.Sources[0].Outcome = (NetDemandCommandOutcomeEnum)999; break;
                case "draft": result.Data.Sources[0].PurchaseDrafts.Clear(); break;
                case "source": result.Data.Sources.Clear(); break; }
            return Task.FromResult(result); };
        using var vm = Create(api); await Open(vm, Row()); var id = vm.ExecutionRequestId;
        await vm.CreateCommand.ExecuteAsync(null); Assert.IsTrue(vm.HasUnknownCreateResult);
        Assert.AreEqual(id, vm.ExecutionRequestId); Assert.AreEqual(1, vm.Items.Count);
    }

    /// <summary>在途写入禁止重复创建和关闭；连接异常结束后只允许原提交重试。</summary>
    [TestMethod]
    public async Task InflightAndThrownTransport_ProtectOriginalRequest()
    {
        var pending = new TaskCompletionSource<ServiceResult<CreatePurchaseDraftsResult>>();
        var api = new DraftApi { Handler = _ => pending.Task }; using var vm = Create(api); await Open(vm, Row());
        var task = vm.CreateCommand.ExecuteAsync(null); Assert.IsFalse(vm.CanClose); Assert.IsFalse(vm.CreateCommand.CanExecute(null));
        pending.SetException(new HttpRequestException("offline")); await task;
        Assert.IsTrue(vm.HasUnknownCreateResult); Assert.IsTrue(vm.CreateCommand.CanExecute(null));
    }

    /// <summary>没有采购读取权限不请求追加接口，仍允许明确新建草稿。</summary>
    [TestMethod]
    public async Task NoReadPermission_AllowsCreateNewWithoutAppendLookup()
    {
        var api = new DraftApi(); using var vm = Create(api, false); await Open(vm, Row());
        Assert.AreEqual(0, api.AppendCalls); Assert.IsTrue(vm.Targets.Single().IsCreateNew);
        Assert.IsTrue(vm.CreateCommand.CanExecute(null)); await vm.CreateCommand.ExecuteAsync(null);
        Assert.AreEqual(PurchaseDraftTargetModeEnum.CreateNew, api.Commands.Single().Sources.Single().Targets.Single().TargetMode);
    }

    /// <summary>选定追加后撤销读取权限也要阻断追加，明确改新建仍可创建。</summary>
    [TestMethod]
    public async Task RevokedReadPermission_PreventsAppendButPreservesExplicitNewChoice()
    {
        var api = new DraftApi { AppendHandler = (_, _, _) => Task.FromResult(ServiceResult<List<SafePurchaseDraftCandidateDto>>.Ok([new() { PurchaseOrderId = Guid.NewGuid(), Version = 1, OrderNo = "PO-APPEND" }])) };
        var user = new CurrentUserStub(true); using var vm = new PurchaseDraftWorkflowViewModel(api, new ProcurementMasterDataApiStub(), new DirectApiCallRunner(), new MessageServiceStub(), currentUser: user);
        await Open(vm, Row()); vm.Targets.Single().SelectedChoice = vm.Targets.Single().Choices[1]; Assert.IsTrue(vm.CreateCommand.CanExecute(null));
        user.Read = false; Assert.IsFalse(vm.CreateCommand.CanExecute(null));
        vm.Targets.Single().SelectedChoice = vm.Targets.Single().Choices[0]; Assert.IsTrue(vm.CreateCommand.CanExecute(null));
    }

    /// <summary>追加候选限定来源及供应商；加载失败保留原追加目标但不能提交，明确改新建才解除。</summary>
    [TestMethod]
    public async Task AppendFailure_DoesNotSilentlySwitchExistingSelectionOrBlockNewDraft()
    {
        var api = new DraftApi(); var row = Row(); var order = Guid.NewGuid();
        api.AppendHandler = (_, _, _) => Task.FromResult(ServiceResult<List<SafePurchaseDraftCandidateDto>>.Ok([new() { PurchaseOrderId = order, Version = 7, OrderNo = "PO-1" }]));
        using var vm = Create(api); await Open(vm, row); var target = vm.Targets.Single(); target.SelectedChoice = target.Choices[1];
        Assert.AreEqual((row.SourceType, row.SourceUid, row.SupplierId), api.LastAppend);
        api.AppendHandler = (_, _, _) => Task.FromResult(ServiceResult<List<SafePurchaseDraftCandidateDto>>.Fail(ServiceErrorCodes.Forbidden, "无读取权限"));
        await vm.UpdateSelectionAsync(new() { Rows = [row] });
        Assert.AreEqual(order, vm.Targets.Single().SelectedChoice!.ExistingPurchaseOrderId);
        Assert.IsFalse(vm.CreateCommand.CanExecute(null)); Assert.IsFalse(vm.Targets.Single().SelectedChoice!.IsAvailable);
        vm.Targets.Single().SelectedChoice = vm.Targets.Single().Choices[0];
        Assert.IsTrue(vm.CreateCommand.CanExecute(null));
    }

    /// <summary>迟到的追加查询不能覆盖更新后的选项和输入。</summary>
    [TestMethod]
    public async Task LateAppendResponse_DoesNotOverwriteNewSelection()
    {
        var api = new DraftApi(); var delayed = new TaskCompletionSource<ServiceResult<List<SafePurchaseDraftCandidateDto>>>();
        api.AppendHandler = (_, _, _) => delayed.Task;
        using var vm = Create(api); vm.BeginSession(); var first = vm.UpdateSelectionAsync(new() { Rows = [Row()] });
        var newer = Row(); api.AppendHandler = (_, _, _) => Task.FromResult(ServiceResult<List<SafePurchaseDraftCandidateDto>>.Ok([]));
        await vm.UpdateSelectionAsync(new() { Rows = [newer] }); delayed.SetResult(ServiceResult<List<SafePurchaseDraftCandidateDto>>.Ok([new() { PurchaseOrderId = Guid.NewGuid(), Version = 1 }]));
        await first; Assert.AreEqual(newer.SourceUid, vm.Targets.Single().SourceUid); Assert.AreEqual(1, vm.Targets.Single().Choices.Count);
    }

    /// <summary>实际供应商、客供送达方和可创建量是本地门禁，不能用显示名称代替身份。</summary>
    [TestMethod]
    public async Task LocalValidation_RejectsMissingSupplierAndExcessQuantity()
    {
        var api = new DraftApi(); using var vm = Create(api);
        await Open(vm, new PurchaseCandidateItemModel { Uid = Guid.NewGuid(), Version = 1, SourceUid = Guid.NewGuid(), SourceType = "SO", RemainingQty = 5, PurchaseQty = 3 });
        Assert.IsFalse(vm.CreateCommand.CanExecute(null)); StringAssert.Contains(vm.CreateBlockReason, "实际采购供应商");
        var row = Row(); row.PurchaseQty = 9; await vm.UpdateSelectionAsync(new() { Rows = [row] });
        Assert.IsFalse(vm.CreateCommand.CanExecute(null)); Assert.AreEqual(0, api.Commands.Count);
    }

    /// <summary>行模式或准备输入变更使本地校验失效，不自动提交旧意图。</summary>
    [TestMethod]
    public async Task ChangedInput_RequiresFreshLocalValidation()
    {
        var api = new DraftApi(); using var vm = Create(api); await Open(vm, Row());
        Assert.IsTrue(vm.CreateCommand.CanExecute(null)); vm.LineMode = PurchaseDraftLineModeEnum.KeepSourceLines;
        Assert.IsFalse(vm.CreateCommand.CanExecute(null)); vm.InvalidatePreparation(); Assert.IsTrue(vm.IsStale);
    }

    private static PurchaseDraftWorkflowViewModel Create(DraftApi api, bool read = true, PurchaseDraftPendingSession? pendingSession = null, CurrentUserStub? user = null)
        => new(api, new ProcurementMasterDataApiStub(), new DirectApiCallRunner(), new MessageServiceStub(), currentUser: user ?? new CurrentUserStub(read), pendingSession: pendingSession);
    private static async Task Open(PurchaseDraftWorkflowViewModel vm, params PurchaseCandidateItemModel[] rows)
    { vm.BeginSession(); await vm.UpdateSelectionAsync(new() { EntryMode = PurchaseDraftEntryModeEnum.ManualSelection, Rows = rows }); }
    private static PurchaseCandidateItemModel Row() => new()
    {
        Uid = Guid.NewGuid(), Version = 4, SourceType = "SO", SourceUid = Guid.NewGuid(), SourceNo = "SO-" + Guid.NewGuid().ToString("N")[..6],
        SupplierId = Guid.NewGuid(), PurchaseSupplierName = "实际供应商", RemainingQty = 5, CreatableQty = 5,
        PurchaseQty = 3, UnitPrice = 7, TaxPrice = 8, PlannedDeliveryDate = new DateTime(2026, 10, 30)
    };
    private static ServiceResult<CreatePurchaseDraftsResult> Success(CreatePurchaseDraftsCommand command) => ServiceResult<CreatePurchaseDraftsResult>.Ok(new()
    {
        ExecutionRequestId = command.ExecutionRequestId, Sources = command.Sources.Select(s => new CreatePurchaseDraftSourceResult
        {
            SourceType = s.SourceType, SourceUid = s.SourceUid, Outcome = NetDemandCommandOutcomeEnum.Succeeded, Message = "已创建",
            PurchaseDrafts = [new() { PurchaseOrderId = Guid.NewGuid(), Version = 1, OrderNo = "PO-NEW" }]
        }).ToList()
    });
    private sealed class DraftApi : IPurchaseDraftApi
    {
        public int PreviewCalls, AppendCalls;
        public int GroupCalls;
        public (string, Guid, Guid) LastAppend;
        public List<CreatePurchaseDraftsCommand> Commands { get; } = [];
        public Func<CreatePurchaseDraftsCommand, Task<ServiceResult<CreatePurchaseDraftsResult>>>? Handler;
        public Func<string, Guid, Guid, Task<ServiceResult<List<SafePurchaseDraftCandidateDto>>>>? AppendHandler;
        public Task<ServiceResult<CreatePurchaseDraftsResult>> CreateAsync(CreatePurchaseDraftsCommand command, CancellationToken cancellationToken = default)
        { Commands.Add(command); return Handler?.Invoke(command) ?? Task.FromResult(Success(command)); }
        public Task<ServiceResult<List<SafePurchaseDraftCandidateDto>>> GetAppendCandidatesAsync(string sourceType, Guid sourceUid, Guid supplierId, CancellationToken cancellationToken = default)
        { AppendCalls++; LastAppend = (sourceType, sourceUid, supplierId); return AppendHandler?.Invoke(sourceType, sourceUid, supplierId) ?? Task.FromResult(ServiceResult<List<SafePurchaseDraftCandidateDto>>.Ok([])); }
        public Task<ServiceResult<PurchaseDraftPreviewResponse>> PreviewAsync(PurchaseDraftPreviewRequest request, CancellationToken cancellationToken = default)
        { PreviewCalls++; throw new AssertFailedException("直接创建流程不得调用预览。"); }
        public Task<ServiceResult<List<PurchaseDraftCandidateSourceGroupDto>>> GetCandidateSourceGroupsAsync(CancellationToken cancellationToken = default)
        { GroupCalls++; return Task.FromResult(ServiceResult<List<PurchaseDraftCandidateSourceGroupDto>>.Ok([])); }
        public Task<ServiceResult<List<PurchaseDraftCandidateDto>>> GetCandidatesAsync(PurchaseDraftCandidateQuery request, CancellationToken cancellationToken = default) => Task.FromResult(ServiceResult<List<PurchaseDraftCandidateDto>>.Ok([]));
    }
    private sealed class CurrentUserStub(bool read) : ICurrentUserService
    {
        public bool Read { get; set; } = read;
        public bool CreateAllowed { get; set; } = true;
        public bool EditAllowed { get; set; } = true;
        public string UserId { get; private set; } = "draft-user";
        public event EventHandler? CurrentUserChanged;
        public bool IsAuthenticated => true;
        public MeDto? CurrentUser => new() { UserId = UserId };
        public string DisplayName => "测试用户";
        public IReadOnlyList<string> Roles => [];
        public IReadOnlyList<string> Permissions => (Read ? new[] { PermissionKeys.ProcurementOrderRead } : [])
            .Concat(CreateAllowed ? new[] { PermissionKeys.ProcurementOrderCreate } : [])
            .Concat(EditAllowed ? new[] { PermissionKeys.ProcurementOrderEdit } : []).ToArray();
        public bool IsAdmin => false;
        public bool HasPermission(string key) => Permissions.Contains(key);
        public void SetCurrentUser(MeDto user) { UserId = user.UserId; Read = user.Permissions.Contains(PermissionKeys.ProcurementOrderRead); CreateAllowed = user.Permissions.Contains(PermissionKeys.ProcurementOrderCreate); EditAllowed = user.Permissions.Contains(PermissionKeys.ProcurementOrderEdit); CurrentUserChanged?.Invoke(this, EventArgs.Empty); }
        public void Clear() { UserId = string.Empty; Read = false; CreateAllowed = false; EditAllowed = false; CurrentUserChanged?.Invoke(this, EventArgs.Empty); }
    }
    private sealed class DirectApiCallRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action(), successMessage);
    }

    private sealed class MessageServiceStub : IMessageService
    {
        public bool AllowErrors { get; init; }
        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public void ShowError(string title, string message)
        {
            if (!AllowErrors) Assert.Fail($"{title}: {message}");
        }
    }

    private sealed class ProcurementMasterDataApiStub : IProcurementMasterDataQueryApi
    {
        public Task<ServiceResult<PagedResponseBase<ProcurementSupplierCandidateDto>>> SearchSuppliersAsync(
            PagedRequestBase request,
            CancellationToken cancellationToken = default) => Task.FromResult(
                ServiceResult<PagedResponseBase<ProcurementSupplierCandidateDto>>.Ok(new PagedResponseBase<ProcurementSupplierCandidateDto>()));

        public Task<ServiceResult<PagedResponseBase<ProcurementCustomerCandidateDto>>> SearchCustomersAsync(PagedRequestBase request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<PagedResponseBase<ProcurementProductCandidateDto>>> SearchProductsAsync(ProcurementProductSearchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<PagedResponseBase<ProcurementCustomerFilterDto>>> SearchCustomerHistoryFiltersAsync(PagedRequestBase request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<PagedResponseBase<ProcurementSupplierFilterDto>>> SearchSupplierHistoryFiltersAsync(PagedRequestBase request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
