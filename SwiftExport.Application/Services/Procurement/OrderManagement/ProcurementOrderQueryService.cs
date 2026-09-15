using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.Wrappers;

using SwiftExport.Core.Interfaces.Procurement;
namespace SwiftExport.Application.Services.Procurement.OrderManagement;

/// <summary>采购管理查询编排，不调用旧详情，也不附加跟进、库存或追溯集合。</summary>
public sealed class ProcurementOrderQueryService(ProcurementOrderTransaction transaction,
    IProcurementOrderQueryRepository queries, ProcurementConfirmationBuilder confirmation,
    IProcurementReversalRepository reversalFacts)
    : IProcurementOrderQueryService
{
    /// <inheritdoc/>
    public Task<ServiceResult<PagedResponseBase<ProcurementOrderListItemDto>>> SearchAsync(ProcurementOrderSearchRequest request, CancellationToken ct = default)
        => transaction.ExecuteAsync("Read", async () =>
        {
            if (request is null || request.PageIndex < 1 || request.PageSize is < 1 or > 200
                || (long)(request.PageIndex - 1) * request.PageSize > int.MaxValue
                || request.Keyword?.Length > 100 || (request.ConfirmationStatus.HasValue && !Enum.IsDefined(request.ConfirmationStatus.Value)))
                throw new InvalidOperationException("采购分页筛选参数无效。");
            return await queries.SearchAsync(request, ct);
        }, ct);
    /// <inheritdoc/>
    public Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailAsync(Guid uid, CancellationToken ct = default)
        => transaction.ExecuteAsync("Read", async () => await queries.GetDetailAsync(uid, ct)
            ?? throw new InvalidOperationException("独立采购单不存在。"), ct);
    /// <inheritdoc/>
    public Task<ServiceResult<ProcurementConfirmationContextDto>> GetConfirmationContextAsync(Guid uid, CancellationToken ct = default)
        => transaction.ExecuteAsync("Read", async () =>
        {
            var facts = await reversalFacts.LockAsync(uid, ct);
            ProcurementConfirmationBuilder.RequireSourceReferences(facts);
            var head = facts.Head ?? throw new InvalidOperationException("采购单不存在。");
            return await confirmation.BuildAsync(head, facts.Lines.Where(x => !x.IsDeleted).ToList(), ct);
        }, ct);
}
