using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Procurement;

namespace SwiftExport.Application.Services.Procurement.PurchaseOrders.Query;

/// <summary>编排采购管理页列表与明细查询，统一执行读取权限与输入边界校验。</summary>
public sealed class PurchaseOrderManagerQueryService(
    ProcurementOrderReadSupport reads,
    IPurchaseOrderManagerQueryRepository repository) : IPurchaseOrderManagerQueryService
{
    /// <inheritdoc/>
    public Task<ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>>> SearchAsync(
        PurchaseOrderManagerSearchRequest request,
        CancellationToken cancellationToken = default)
        => reads.ReadAsync("Read", async () =>
        {
            ValidateSearchRequest(request);
            return await repository.SearchAsync(request, cancellationToken);
        }, cancellationToken);

    /// <inheritdoc/>
    public Task<ServiceResult<IReadOnlyList<PurchaseOrderManagerLineDto>>> GetLinesAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken = default)
        => reads.ReadAsync("Read", async () =>
        {
            if (purchaseOrderId == Guid.Empty)
                throw new InvalidOperationException("采购单标识无效。");

            return await repository.GetLinesAsync(purchaseOrderId, cancellationToken);
        }, cancellationToken);

    /// <summary>限制分页、文本、日期和共享枚举，避免无界查询及无效状态穿透到 SQL。</summary>
    private static void ValidateSearchRequest(PurchaseOrderManagerSearchRequest request)
    {
        if (request is null
            || request.PageIndex < 1
            || request.PageSize is < 1 or > 200
            || (long)(request.PageIndex - 1) * request.PageSize > int.MaxValue
            || HasOversizedText(request.Keyword)
            || HasOversizedText(request.CustomerCode)
            || request.SupplierId == Guid.Empty
            || request.OrderDateFrom?.Date > request.OrderDateTo?.Date
            || request.ConfirmationStatuses is null
            || request.LifecycleStatuses is null
            || request.ReadyStatuses is null
            || request.ReceiptStatuses is null
            || request.ConfirmationStatuses.Count > 20
            || request.LifecycleStatuses.Count > 20
            || request.ReadyStatuses.Count > 20
            || request.ReceiptStatuses.Count > 20
            || request.ConfirmationStatuses.Any(status => !Enum.IsDefined(status))
            || request.LifecycleStatuses.Any(status => !Enum.IsDefined(status))
            || request.ReadyStatuses.Any(status => !Enum.IsDefined(status))
            || request.ReceiptStatuses.Any(status => !Enum.IsDefined(status))
            || !Enum.IsDefined(request.DueView)
            || request.PlannedDeliveryDateFrom > request.PlannedDeliveryDateTo
            || request.ExpectedCompletionDateFrom > request.ExpectedCompletionDateTo)
            throw new InvalidOperationException("采购管理筛选参数无效。");
    }

    /// <summary>管理页模糊查询统一限制为一百字符；空文本不构成限制。</summary>
    private static bool HasOversizedText(string? value) => value?.Length > 100;
}
