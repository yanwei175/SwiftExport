using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Services;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

public sealed partial class PurchaseOrderManagerBatchConfirmationTests
{
    /// <summary>整草稿删除需要编辑权限及服务端资格，确认权限不能替代编辑权限。</summary>
    [TestMethod]
    public async Task DeleteDraft_RequiresEditAndServerEligibility()
    {
        var row = Order("PO-DRAFT"); row.CanDeleteDraft = true;
        var denied = CreateFixture([row], true, includeEditPermission: false);
        await denied.ViewModel.InitializeAsync(); denied.ViewModel.SelectedOrder = denied.ViewModel.Orders.Single();
        Assert.IsFalse(denied.ViewModel.DeleteDraftCommand.CanExecute(null));
        var allowed = CreateFixture([row], false, includeEditPermission: true);
        await allowed.ViewModel.InitializeAsync(); allowed.ViewModel.SelectedOrder = allowed.ViewModel.Orders.Single();
        Assert.IsTrue(allowed.ViewModel.DeleteDraftCommand.CanExecute(null));
        row.CanDeleteDraft = false; row.DeleteDraftBlockReason = "曾确认草稿须作废。";
        var historical = CreateFixture([row], false, includeEditPermission: true);
        await historical.ViewModel.InitializeAsync(); historical.ViewModel.SelectedOrder = historical.ViewModel.Orders.Single();
        Assert.IsFalse(historical.ViewModel.DeleteDraftCommand.CanExecute(null));
        Assert.AreEqual(row.DeleteDraftBlockReason, historical.ViewModel.SelectedOrder.DeleteDraftBlockReason);
    }

    /// <summary>管理页读取所有当前行并用专用预览删除，成功后从列表移除该采购。</summary>
    [TestMethod]
    public async Task DeleteDraft_ManagerUsesAllLinesAndRemovesDeletedOrder()
    {
        var row = Order("PO-DELETE"); row.CanDeleteDraft = true;
        var fixture = CreateFixture([row], false, includeEditPermission: true, reversalDialogs: new DeleteDialog());
        var lines = new List<PurchaseOrderManagerLineDto> { new() { PurchaseOrderLineId = Guid.NewGuid(), Version = 3 }, new() { PurchaseOrderLineId = Guid.NewGuid(), Version = 7 } };
        fixture.Manager.LinesHandler = (_, _) => Task.FromResult(ServiceResult<List<PurchaseOrderManagerLineDto>>.Ok(lines));
        fixture.Orders.DeleteDraftPreviewHandler = (uid, request) =>
        {
            Assert.AreEqual(row.Version, request.ExpectedVersion); Assert.AreEqual(2, request.Lines.Count);
            CollectionAssert.AreEquivalent(lines.Select(x => x.PurchaseOrderLineId).ToArray(), request.Lines.Select(x => x.PurchaseOrderLineId).ToArray());
            return Task.FromResult(ServiceResult<ProcurementReversalPreviewDto>.Ok(DeletePreview(uid, request)));
        };
        DeleteProcurementDraftCommand? sent = null;
        fixture.Orders.DeleteDraftHandler = (uid, command) =>
        {
            sent = command; fixture.Manager.SearchHandler = request => Task.FromResult(Page([], request.PageIndex, request.PageSize));
            return Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Ok(DeletedReceipt(uid, command)));
        };
        await fixture.ViewModel.InitializeAsync(); fixture.ViewModel.SelectedOrder = fixture.ViewModel.Orders.Single();
        await fixture.ViewModel.DeleteDraftCommand.ExecuteAsync(null);
        Assert.IsNotNull(sent); Assert.AreEqual(2, sent.Lines.Count); Assert.AreEqual("删除未确认草稿", sent.Reason);
        Assert.AreEqual(0, fixture.ViewModel.Orders.Count); StringAssert.Contains(fixture.ViewModel.ResultSummary, "占用已释放");
    }

    /// <summary>详情删除成功采用删除回执，不再读取已删除详情，保留可关闭的删除状态。</summary>
    [TestMethod]
    public async Task DeleteDraft_DetailAcceptsDeletedStateWithoutReread()
    {
        var fixture = CreateFixture(Order("PO-DETAIL")); var uid = Guid.NewGuid(); var reads = 0;
        fixture.Orders.DetailHandler = id => { reads++; var dto = Detail(id); dto.CanDeleteDraft = true;
            return Task.FromResult(ServiceResult<SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto.PurchaseOrderDetailDto>.Ok(dto)); };
        fixture.Orders.DeleteDraftPreviewHandler = (id, request) => Task.FromResult(ServiceResult<ProcurementReversalPreviewDto>.Ok(DeletePreview(id, request)));
        fixture.Orders.DeleteDraftHandler = (id, command) => Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Ok(DeletedReceipt(id, command)));
        using var detail = CreateDetail(fixture, new DeleteDialog());
        await detail.InitializeDocumentAsync(new PurchaseOrderDocumentOpenRequest { PurchaseOrderId = uid }, CancellationToken.None);
        Assert.IsTrue(detail.DeleteDraftCommand.CanExecute(null)); await detail.DeleteDraftCommand.ExecuteAsync(null);
        Assert.IsTrue(detail.IsDeleted); Assert.AreEqual(1, reads); Assert.AreEqual(0, detail.Lines.Items.Count);
        Assert.IsFalse(detail.CanEdit); Assert.IsFalse(detail.DeleteDraftCommand.CanExecute(null));
        Assert.IsTrue(await detail.CanCloseAsync(CancellationToken.None));
        await detail.RefreshCommand.ExecuteAsync(null); Assert.AreEqual(1, reads);
    }

    private static ProcurementReversalPreviewDto DeletePreview(Guid uid, ProcurementReversalPreviewRequest request) => new()
    {
        PurchaseOrderId = uid, Version = request.ExpectedVersion, Operation = ProcurementReversalOperation.DeleteDraft, CanExecute = true,
        Lines = request.Lines.Select(x => new ProcurementReversalLineDto { PurchaseOrderLineId = x.PurchaseOrderLineId, Version = x.Version }).ToList()
    };
    private static ProcurementOrderCommandResult DeletedReceipt(Guid uid, DeleteProcurementDraftCommand command) => new()
    {
        PurchaseOrderId = uid, Version = command.ExpectedVersion + 1, IsDeleted = true, ConfirmationStatus = ConfirmationStatusEnum.待确认,
        LifecycleStatus = PurchaseOrderLifecycleStatusEnum.Active,
        Lines = command.Lines.Select(x => new ProcurementLineVersionDto { PurchaseOrderLineId = x.PurchaseOrderLineId, Version = x.Version + 1 }).ToList()
    };
    private sealed class DeleteDialog : IProcurementReversalDialogService
    {
        public Task<string?> ConfirmAsync(ProcurementReversalPreviewDto preview, CancellationToken ct = default) => Task.FromResult<string?>(preview.CanExecute ? "删除未确认草稿" : null);
    }
}
