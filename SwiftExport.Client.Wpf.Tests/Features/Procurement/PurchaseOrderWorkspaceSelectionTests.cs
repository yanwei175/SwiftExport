using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

/// <summary>验证采购工作台选择切换、多选条件和取消确认抽屉的前端编排行为。</summary>
[TestClass]
public sealed class PurchaseOrderWorkspaceSelectionTests
{
    /// <summary>验证慢请求在快速切换后完成时，不能覆盖最新采购单的明细。</summary>
    [TestMethod]
    public async Task SelectionChange_SlowOldRequestMustNotOverrideLatestLines()
    {
        var api = new PurchaseOrderWorkspaceApiStub();
        var viewModel = CreateViewModel(api, new MessageServiceStub());
        var slowOrder = CreateOrder(api.SlowOrderId, "PO-SLOW");
        var fastOrder = CreateOrder(api.FastOrderId, "PO-FAST");

        viewModel.SelectedOrder = slowOrder;
        await api.SlowRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.SelectedOrder = fastOrder;
        await WaitUntilAsync(() => viewModel.Lines.Count == 1 && viewModel.Lines[0].Sku == "FAST-SKU");

        api.CompleteSlowRequest();
        await Task.Delay(50);

        Assert.AreEqual(fastOrder.Uid, viewModel.SelectedOrder?.Uid);
        Assert.HasCount(1, viewModel.Lines);
        Assert.AreEqual("FAST-SKU", viewModel.Lines[0].Sku);
    }

    /// <summary>验证三个状态多选集合会完整映射到采购查询请求。</summary>
    [TestMethod]
    public async Task Search_MustMapAllSelectedStatusValues()
    {
        var api = new PurchaseOrderWorkspaceApiStub();
        var viewModel = CreateViewModel(api, new MessageServiceStub());
        viewModel.SelectedConfirmationStatuses.Add(viewModel.ConfirmationOptions.Single(x => x.Value == ConfirmationStatusEnum.已确认));
        viewModel.SelectedLifecycleStatuses.Add(viewModel.LifecycleOptions.Single(x => x.Value == PurchaseOrderLifecycleStatusEnum.Active));
        viewModel.SelectedArrangementStatuses.Add(viewModel.ArrangementOptions.Single(x => x.Value == SupplierArrangementStatusEnum.Unknown));
        viewModel.SelectedArrangementStatuses.Add(viewModel.ArrangementOptions.Single(x => x.Value == SupplierArrangementStatusEnum.NotArranged));

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.IsNotNull(api.LastSearchRequest);
        CollectionAssert.AreEqual(new[] { ConfirmationStatusEnum.已确认 }, api.LastSearchRequest.ConfirmationStatuses);
        CollectionAssert.AreEqual(new[] { PurchaseOrderLifecycleStatusEnum.Active }, api.LastSearchRequest.LifecycleStatuses);
        CollectionAssert.AreEquivalent(
            new[] { SupplierArrangementStatusEnum.Unknown, SupplierArrangementStatusEnum.NotArranged },
            api.LastSearchRequest.ArrangementStatuses);
    }

    /// <summary>验证清空筛选会清空三个状态集合，并以无状态条件重新查询第一页。</summary>
    [TestMethod]
    public async Task ClearFilters_MustClearAllStatusSelections()
    {
        var api = new PurchaseOrderWorkspaceApiStub();
        var viewModel = CreateViewModel(api, new MessageServiceStub());
        viewModel.SelectedConfirmationStatuses.Add(viewModel.ConfirmationOptions[0]);
        viewModel.SelectedLifecycleStatuses.Add(viewModel.LifecycleOptions[0]);
        viewModel.SelectedArrangementStatuses.Add(viewModel.ArrangementOptions[0]);

        await viewModel.ClearFiltersCommand.ExecuteAsync(null);

        Assert.HasCount(0, viewModel.SelectedConfirmationStatuses);
        Assert.HasCount(0, viewModel.SelectedLifecycleStatuses);
        Assert.HasCount(0, viewModel.SelectedArrangementStatuses);
        Assert.IsNotNull(api.LastSearchRequest);
        Assert.HasCount(0, api.LastSearchRequest.ConfirmationStatuses);
        Assert.HasCount(0, api.LastSearchRequest.LifecycleStatuses);
        Assert.HasCount(0, api.LastSearchRequest.ArrangementStatuses);
    }

    /// <summary>验证顶部取消确认只打开抽屉，空原因不会调用后端。</summary>
    [TestMethod]
    public async Task CancelConfirmation_MustOpenDrawerAndRequireReasonBeforeApiCall()
    {
        var api = new PurchaseOrderWorkspaceApiStub();
        var messages = new MessageServiceStub();
        var viewModel = CreateViewModel(api, messages);
        viewModel.SelectedOrder = CreateOrder(api.FastOrderId, "PO-FAST", canCancelConfirmation: true);

        viewModel.PrepareCancelConfirmationCommand.Execute(null);
        await viewModel.CancelConfirmationCommand.ExecuteAsync(null);

        Assert.IsTrue(viewModel.IsDrawerOpen);
        Assert.AreEqual(1, messages.WarningCount);
        Assert.AreEqual(0, api.CancelConfirmationCount);
    }

    /// <summary>验证累计数量越界会在前端阻断，且不会发出供应商反馈请求。</summary>
    [TestMethod]
    public async Task ReportProgress_QuantityOutsidePurchaseRangeMustBeBlocked()
    {
        var api = new PurchaseOrderWorkspaceApiStub();
        var messages = new MessageServiceStub();
        var viewModel = CreateProgressViewModel(api, messages);
        viewModel.SelectedOrder = CreateOrder(api.FastOrderId, "PO-FAST", canReportProgress: true);
        viewModel.SelectedLine = SwiftExportClient.Features.Procurement.PurchaseOrders.Models.PurchaseOrderLineEditorModel.FromDto(
            new PurchaseOrderLineDetailDto { Uid = Guid.NewGuid(), Version = 1, LineNumber = 1, Quantity = 10m });
        viewModel.ReportReadyQty = 10.0001m;

        await viewModel.ReportProgressCommand.ExecuteAsync(null);

        Assert.AreEqual(1, messages.WarningCount);
        Assert.AreEqual(0, api.ReportProgressCount);
    }

    /// <summary>验证累计数量下降时持续暴露强纠错提示，并明确旧历史不会被覆盖。</summary>
    [TestMethod]
    public void ReportProgress_DecreaseMustExposeCorrectionWarning()
    {
        var viewModel = CreateProgressViewModel(new PurchaseOrderWorkspaceApiStub(), new MessageServiceStub());
        viewModel.SelectedLine = SwiftExportClient.Features.Procurement.PurchaseOrders.Models.PurchaseOrderLineEditorModel.FromDto(
            new PurchaseOrderLineDetailDto
            {
                Quantity = 10m,
                ProgressHistory = [new SupplierProgressReportDto { Sequence = 1, ReadyQty = 6m }]
            });
        viewModel.ReportReadyQty = 5m;

        Assert.IsTrue(viewModel.IsProgressCorrection);
        StringAssert.Contains(viewModel.ProgressCorrectionWarning, "新增纠错报告");
        StringAssert.Contains(viewModel.ProgressCorrectionWarning, "保留原历史");
    }

    /// <summary>验证独立跟进页默认只查询活动且已确认采购单。</summary>
    [TestMethod]
    public async Task ProgressWorkspace_DefaultAndResetSearchMustUseActionableStatuses()
    {
        var api = new PurchaseOrderWorkspaceApiStub();
        var viewModel = CreateProgressViewModel(api, new MessageServiceStub());

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.IsNotNull(api.LastSearchRequest);
        CollectionAssert.AreEqual(new[] { ConfirmationStatusEnum.已确认 }, api.LastSearchRequest.ConfirmationStatuses);
        CollectionAssert.AreEqual(new[] { PurchaseOrderLifecycleStatusEnum.Active }, api.LastSearchRequest.LifecycleStatuses);

        viewModel.SelectedConfirmationStatuses.Clear();
        viewModel.SelectedLifecycleStatuses.Clear();
        await viewModel.SearchCommand.ExecuteAsync(null);
        Assert.HasCount(0, api.LastSearchRequest!.ConfirmationStatuses);
        Assert.HasCount(0, api.LastSearchRequest.LifecycleStatuses);

        await viewModel.ResetFiltersCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { ConfirmationStatusEnum.已确认 }, api.LastSearchRequest!.ConfirmationStatuses);
        CollectionAssert.AreEqual(new[] { PurchaseOrderLifecycleStatusEnum.Active }, api.LastSearchRequest.LifecycleStatuses);
    }

    /// <summary>验证跟进页快速切换采购单时，慢旧请求不能覆盖最新采购行。</summary>
    [TestMethod]
    public async Task ProgressWorkspace_SlowOldRequestMustNotOverrideLatestLines()
    {
        var api = new PurchaseOrderWorkspaceApiStub();
        var viewModel = CreateProgressViewModel(api, new MessageServiceStub());
        var slowOrder = CreateOrder(api.SlowOrderId, "PO-SLOW", canReportProgress: true);
        var fastOrder = CreateOrder(api.FastOrderId, "PO-FAST", canReportProgress: true);

        viewModel.SelectedOrder = slowOrder;
        var slowLoad = viewModel.LoadSelectedOrderCommand.ExecuteAsync(null);
        await api.SlowRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.SelectedOrder = fastOrder;
        await viewModel.LoadSelectedOrderCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => viewModel.Lines.Count == 1 && viewModel.Lines[0].Sku == "FAST-SKU");

        api.CompleteSlowRequest();
        await slowLoad;

        Assert.AreEqual(fastOrder.Uid, viewModel.SelectedOrder?.Uid);
        Assert.HasCount(1, viewModel.Lines);
        Assert.AreEqual("FAST-SKU", viewModel.Lines[0].Sku);
    }

    /// <summary>验证登记成功后重新查询采购头行，并恢复原采购单和采购行选择。</summary>
    [TestMethod]
    public async Task ProgressWorkspace_SuccessfulReportMustRefreshAndPreserveSelection()
    {
        var api = new PurchaseOrderWorkspaceApiStub { CompleteReports = true };
        var order = CreateOrder(api.FastOrderId, "PO-FAST", canReportProgress: true);
        api.SearchItems.Add(order);
        var viewModel = CreateProgressViewModel(api, new MessageServiceStub(), new ConfirmDialogServiceStub());

        await viewModel.SearchCommand.ExecuteAsync(null);
        viewModel.ReportReadyQty = 1m;
        await viewModel.ReportProgressCommand.ExecuteAsync(null);

        Assert.AreEqual(1, api.ReportProgressCount);
        Assert.AreEqual(order.Uid, viewModel.SelectedOrder?.Uid);
        Assert.AreEqual(api.FastLineId, viewModel.SelectedLine?.Uid);
        Assert.IsTrue(api.SearchCount >= 2);
    }

    private static PurchaseOrderWorkspaceViewModel CreateViewModel(PurchaseOrderWorkspaceApiStub api, IMessageService messages) =>
        new(api, new DirectApiCallRunner(), null!, messages, null!, null!, null!, null!);

    private static PurchaseOrderProgressWorkspaceViewModel CreateProgressViewModel(
        PurchaseOrderWorkspaceApiStub api,
        IMessageService messages,
        IDialogService? dialogs = null) =>
        new(api, new DirectApiCallRunner(), dialogs!, messages);

    private static PurchaseOrderListItemDto CreateOrder(
        Guid uid,
        string orderNo,
        bool canCancelConfirmation = false,
        bool canReportProgress = false) => new()
    {
        Uid = uid,
        Version = 1,
        OrderNo = orderNo,
        CanCancelConfirmation = canCancelConfirmation,
        CanReportProgress = canReportProgress
    };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++) await Task.Delay(10);
        Assert.IsTrue(condition(), "等待采购明细加载超时。");
    }

    private sealed class PurchaseOrderWorkspaceApiStub : IPurchaseOrderWorkspaceApi
    {
        private readonly TaskCompletionSource<ServiceResult<PurchaseOrderDetailDto>> _slowResult =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Guid SlowOrderId { get; } = Guid.NewGuid();
        public Guid FastOrderId { get; } = Guid.NewGuid();
        public Guid FastLineId { get; } = Guid.NewGuid();
        public TaskCompletionSource SlowRequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<PurchaseOrderListItemDto> SearchItems { get; } = [];
        public PurchaseOrderSearchRequest? LastSearchRequest { get; private set; }
        public bool CompleteReports { get; init; }
        public int SearchCount { get; private set; }
        public int CancelConfirmationCount { get; private set; }
        public int ReportProgressCount { get; private set; }

        public Task<ServiceResult<PagedResponseBase<PurchaseOrderListItemDto>>> SearchAsync(PurchaseOrderSearchRequest request, CancellationToken cancellationToken = default)
        {
            SearchCount++;
            LastSearchRequest = request;
            return Task.FromResult(ServiceResult<PagedResponseBase<PurchaseOrderListItemDto>>.Ok(new PagedResponseBase<PurchaseOrderListItemDto>
            {
                Items = SearchItems, PageIndex = request.PageIndex, PageSize = request.PageSize, TotalCount = SearchItems.Count
            }));
        }

        public Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default)
        {
            if (uid == SlowOrderId)
            {
                SlowRequestStarted.TrySetResult();
                return _slowResult.Task;
            }

            return Task.FromResult(ServiceResult<PurchaseOrderDetailDto>.Ok(CreateDetail("FAST-SKU", FastLineId)));
        }

        public void CompleteSlowRequest() => _slowResult.TrySetResult(ServiceResult<PurchaseOrderDetailDto>.Ok(CreateDetail("SLOW-SKU", Guid.NewGuid())));

        public Task<ServiceResult<ProcurementCommandResult>> CancelConfirmationAsync(Guid uid, CancelPurchaseOrderConfirmationCommand command, CancellationToken cancellationToken = default)
        {
            CancelConfirmationCount++;
            return NotSupported<ProcurementCommandResult>();
        }

        public Task<ServiceResult<PurchaseOrderTraceDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default) => NotSupported<PurchaseOrderTraceDto>();
        public Task<ServiceResult<ProcurementCommandResult>> UpdateDraftAsync(Guid uid, UpdatePurchaseOrderDraftCommand command, CancellationToken cancellationToken = default) => NotSupported<ProcurementCommandResult>();
        public Task<ServiceResult<ProcurementCommandResult>> ConfirmAsync(Guid uid, ConfirmPurchaseOrderCommand command, CancellationToken cancellationToken = default) => NotSupported<ProcurementCommandResult>();
        public Task<ServiceResult<IReadOnlyList<SupplierProgressReportDto>>> GetProgressAsync(Guid uid, CancellationToken cancellationToken = default) => NotSupported<IReadOnlyList<SupplierProgressReportDto>>();
        public Task<ServiceResult<ProcurementCommandResult>> ReportProgressAsync(Guid uid, SupplierProgressReportCommand command, CancellationToken cancellationToken = default)
        {
            ReportProgressCount++;
            return CompleteReports
                ? Task.FromResult(ServiceResult<ProcurementCommandResult>.Ok(new ProcurementCommandResult { PurchaseOrderId = uid }))
                : NotSupported<ProcurementCommandResult>();
        }

        private static PurchaseOrderDetailDto CreateDetail(string sku, Guid lineId) => new()
        {
            Lines = [new PurchaseOrderLineDetailDto { Uid = lineId, Version = 1, LineNumber = 1, Sku = sku, Quantity = 1 }]
        };

        private static Task<ServiceResult<T>> NotSupported<T>() => Task.FromException<ServiceResult<T>>(new NotSupportedException());
    }

    private sealed class DirectApiCallRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action(), successMessage);
    }

    private sealed class MessageServiceStub : IMessageService
    {
        public int WarningCount { get; private set; }
        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) => WarningCount++;
        public void ShowError(string title, string message) => Assert.Fail($"{title}: {message}");
    }

    private sealed class ConfirmDialogServiceStub : IDialogService
    {
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
