using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.Wrappers;

using SwiftExport.Core.Interfaces.Procurement;
namespace SwiftExport.Application.Services.Procurement.PurchaseOrders.Query;

/// <summary>采购列表与订单详情查询编排，统一读取权限；跟进、库存和追溯集合由各自查询用例提供。</summary>
public sealed class ProcurementOrderQueryService(ProcurementOrderReadSupport reads,
    IProcurementOrderQueryRepository queries)
    : IProcurementOrderQueryService
{
    /// <inheritdoc/>
    public Task<ServiceResult<PagedResponseBase<ProcurementOrderListItemDto>>> SearchAsync(ProcurementOrderSearchRequest request, CancellationToken ct = default)
        => reads.ReadAsync("Read", async () =>
        {
            if (request is null || request.PageIndex < 1 || request.PageSize is < 1 or > 200
                || (long)(request.PageIndex - 1) * request.PageSize > int.MaxValue
                || request.Keyword?.Length > 100 || (request.ConfirmationStatus.HasValue && !Enum.IsDefined(request.ConfirmationStatus.Value)))
                throw new InvalidOperationException("采购分页筛选参数无效。");
            return await queries.SearchAsync(request, ct);
        }, ct);
    /// <inheritdoc/>
    public Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailAsync(Guid uid, CancellationToken ct = default)
        => reads.ReadAsync("Read", async () => await queries.GetDetailAsync(uid, ct)
            ?? throw new InvalidOperationException("独立采购单不存在。"), ct);
}
