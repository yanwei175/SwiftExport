using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Application.Services.Procurement.PurchaseOrders.Progress;

/// <summary>采购供应商跟进查询；只读取头摘要、采购行和追加历史。</summary>
public sealed class PurchaseOrderProgressQueryService(
    ProcurementOrderReadSupport reads, IPurchaseOrderProgressQueryRepository queries)
    : IPurchaseOrderProgressQueryService
{
    /// <inheritdoc/>
    public Task<ServiceResult<PagedResponseBase<PurchaseOrderProgressListItemDto>>> SearchAsync(
        PurchaseOrderProgressSearchRequest request,
        CancellationToken cancellationToken = default)
        => reads.ReadAsync("ProgressView", async () =>
        {
            ValidateSearchRequest(request);
            return await queries.SearchAsync(request, cancellationToken);
        }, cancellationToken);

    /// <inheritdoc/>
    public Task<ServiceResult<PurchaseOrderProgressDetailDto>> GetDetailAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken = default)
        => reads.ReadAsync("ProgressView", async () =>
        {
            if (purchaseOrderId == Guid.Empty)
                throw ProcurementOperationException.Validation("采购单标识不能为空。", "PurchaseOrderId");
            return await queries.GetDetailAsync(purchaseOrderId, cancellationToken)
                ?? throw ProcurementOperationException.Blocked("采购单不存在。");
        }, cancellationToken);

    /// <summary>验证分页、文本、状态集合及日期范围，避免无界或无效筛选进入 SQL。</summary>
    private static void ValidateSearchRequest(PurchaseOrderProgressSearchRequest request)
    {
        if (request is null
            || request.PageIndex < 1
            || request.PageSize is < 1 or > 200
            || (long)(request.PageIndex - 1) * request.PageSize > int.MaxValue
            || request.ConfirmationStatuses is null
            || request.LifecycleStatuses is null
            || request.ArrangementStatuses is null
            || request.ConfirmationStatuses.Count > 20
            || request.LifecycleStatuses.Count > 20
            || request.ArrangementStatuses.Count > 20
            || request.ConfirmationStatuses.Any(status => !Enum.IsDefined(status))
            || request.LifecycleStatuses.Any(status => !Enum.IsDefined(status))
            || request.ArrangementStatuses.Any(status => !Enum.IsDefined(status))
            || HasOversizedText(request.Keyword)
            || HasOversizedText(request.OrderNo)
            || HasOversizedText(request.SupplierKeyword)
            || HasOversizedText(request.Sku)
            || request.PlannedDeliveryDateFrom > request.PlannedDeliveryDateTo
            || request.ExpectedReadyDateFrom > request.ExpectedReadyDateTo)
            throw ProcurementOperationException.Validation("采购跟进筛选参数无效。");
    }

    private static bool HasOversizedText(string? value) => value?.Length > 100;

}
