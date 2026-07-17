using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;
using SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.DemandPlanning.NetDemand;

[TestClass]
public sealed class NetDemandPlanEditorViewModelTests
{
    [TestMethod]
    public async Task PartialSuccess_ShouldNotifySavedSoWorkspaceCanRefreshCommittedVersions()
    {
        var api = new NetDemandApiStub
        {
            BatchResult = new NetDemandPlanCommandResult
            {
                Items =
                {
                    new NetDemandPlanCommandItemResult { Outcome = NetDemandCommandOutcomeEnum.Succeeded, Message = "已保存" },
                    new NetDemandPlanCommandItemResult { Outcome = NetDemandCommandOutcomeEnum.Failed, Message = "版本冲突" },
                },
            },
        };
        var messages = new MessageServiceStub();
        using var viewModel = new NetDemandPlanEditorViewModel(api, null!, new DirectApiCallRunner(), messages);
        var rows = new[]
        {
            CreateRow("A-1"),
            CreateRow("A-2"),
        };
        var savedCount = 0;
        viewModel.Saved += (_, _) => savedCount++;
        viewModel.OpenBatch(rows, isReadOnly: false);
        viewModel.SelectedSupplier = new SupplierDto { Uid = Guid.NewGuid(), SupplierCode = "S001", SupplierName = "供应商一" };
        viewModel.Reason = "批量调整";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.AreEqual(1, savedCount, "部分成功时必须通知工作台刷新，不能继续持有旧 Allocation Version。");
        Assert.IsFalse(viewModel.IsDirty);
        StringAssert.Contains(messages.LastWarning ?? string.Empty, "失败 1");
    }

    [TestMethod]
    public void OpenParentPlan_ShouldExposeAffectedComponentsAndDisableSaveWhenExecutionBlockersExist()
    {
        using var viewModel = new NetDemandPlanEditorViewModel(
            new NetDemandApiStub(), null!, new DirectApiCallRunner(), new MessageServiceStub());
        var detail = CreateBlockedDetail(isCustomerSupplied: false);
        detail.RelatedCustomerSuppliedPlans.Add(new NetDemandListItemDto
        {
            Uid = Guid.NewGuid(),
            Sku = "B-100",
            PurchaseSupplierName = "配件供应商乙",
            DeliveryTargetSupplierName = "父件供应商甲",
        });

        viewModel.OpenSingle(detail, isReadOnly: false);
        viewModel.Reason = "调整父件供应商";

        Assert.HasCount(1, viewModel.RelatedCustomerSuppliedPlans);
        Assert.AreEqual("B-100", viewModel.RelatedCustomerSuppliedPlans[0].Sku);
        Assert.HasCount(1, viewModel.BlockingExecutionLinks);
        Assert.AreEqual("PO-1001", viewModel.BlockingExecutionLinks[0].TargetNo);
        Assert.AreEqual(2, viewModel.BlockingExecutionLinks[0].TargetLineNumber);
        Assert.AreEqual(6m, viewModel.BlockingExecutionLinks[0].AllocatedQty);
        Assert.IsFalse(viewModel.SaveCommand.CanExecute(null));
        StringAssert.Contains(viewModel.StatusText, "取消安全采购草稿");
    }

    [TestMethod]
    public void OpenCustomerSuppliedPlan_ShouldAlsoExposeOwnExecutionBlockers()
    {
        using var viewModel = new NetDemandPlanEditorViewModel(
            new NetDemandApiStub(), null!, new DirectApiCallRunner(), new MessageServiceStub());

        viewModel.OpenSingle(CreateBlockedDetail(isCustomerSupplied: true), isReadOnly: false);

        Assert.AreEqual("客供件", viewModel.DemandRoleText);
        Assert.HasCount(1, viewModel.BlockingExecutionLinks);
        Assert.IsTrue(viewModel.HasBlockingExecutionLinks);
        Assert.IsFalse(viewModel.SaveCommand.CanExecute(null));
    }

    private static NetDemandRowModel CreateRow(string sku) => NetDemandRowModel.FromDto(new NetDemandListItemDto
    {
        Uid = Guid.NewGuid(),
        AllocationId = Guid.NewGuid(),
        AllocationVersion = 1,
        Sku = sku,
        ExecutionType = DemandExecutionTypeEnum.Purchase,
        PlannedQty = 1m,
        CanReplacePlan = true,
    });

    private static NetDemandDetailDto CreateBlockedDetail(bool isCustomerSupplied)
    {
        var allocationId = Guid.NewGuid();
        return new NetDemandDetailDto
        {
            Summary = new NetDemandListItemDto
            {
                Uid = Guid.NewGuid(),
                SourceNo = "SO-1001",
                SourceLineNumber = 1,
                Sku = isCustomerSupplied ? "B-100" : "A-100",
                IsCustomerSupplied = isCustomerSupplied,
            },
            ActiveAllocation = new NetDemandAllocationDto
            {
                Uid = allocationId,
                Version = 3,
                PlannedQty = 10m,
                ExecutionType = DemandExecutionTypeEnum.Purchase,
                PurchaseSupplierId = Guid.NewGuid(),
                PurchaseSupplierCode = "S001",
                PurchaseSupplierName = "供应商甲",
            },
            BlockingExecutionLinks =
            {
                new NetDemandExecutionLinkDto
                {
                    Uid = Guid.NewGuid(),
                    NetDemandAllocationId = allocationId,
                    TargetNo = "PO-1001",
                    TargetLineNumber = 2,
                    AllocatedQty = 6m,
                    LinkStatus = DemandExecutionLinkStatusEnum.Active,
                },
            },
        };
    }

    private sealed class NetDemandApiStub : INetDemandWorkspaceApi
    {
        public NetDemandPlanCommandResult BatchResult { get; set; } = new();

        public Task<ServiceResult<NetDemandPlanCommandResult>> BatchAssignSupplierAsync(BatchAssignSupplierCommand command) =>
            Task.FromResult(ServiceResult<NetDemandPlanCommandResult>.Ok(BatchResult));

        public Task<ServiceResult<PagedResponseBase<NetDemandListItemDto>>> SearchAsync(NetDemandSearchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<NetDemandDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<NetDemandTraceDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<NetDemandPlanCommandResult>> ReplacePlanAsync(ReplaceNetDemandPlanCommand command) => throw new NotSupportedException();
        public Task<ServiceResult<NetDemandPlanCommandResult>> ApplyDefaultSupplierAsync(ApplyProductDefaultSupplierCommand command) => throw new NotSupportedException();
        public Task<ServiceResult<PurchaseDraftPreviewResponse>> PreviewPurchaseDraftsAsync(PurchaseDraftPreviewRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<CreatePurchaseDraftsResult>> CreatePurchaseDraftsAsync(CreatePurchaseDraftsCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<PurchaseDraftLifecycleResult>> CancelPurchaseDraftAsync(Guid uid, CancelPurchaseDraftCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<PurchaseDraftReleasePreviewResponse>> PreviewPurchaseDraftReleaseAsync(Guid uid, PurchaseDraftReleasePreviewRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<PurchaseDraftLifecycleResult>> ReleasePurchaseDraftAsync(Guid uid, ReleasePurchaseDraftCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<SourcePurchaseTraceDto>> GetPurchaseTraceBySourceAsync(string sourceType, Guid sourceUid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class DirectApiCallRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action(), successMessage);
    }

    private sealed class MessageServiceStub : IMessageService
    {
        public string? LastWarning { get; private set; }
        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) => LastWarning = message;
        public void ShowError(string title, string message) => Assert.Fail($"{title}: {message}");
    }
}
