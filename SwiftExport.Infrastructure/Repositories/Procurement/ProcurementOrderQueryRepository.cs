using Dapper;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Contexts;
namespace SwiftExport.Infrastructure.Repositories.Procurement;

/// <summary>新采购管理专用 SQL 投影，只选择本用例字段；详情不联接需求、报告或库存。</summary>
public sealed class ProcurementOrderQueryRepository(RepositoryBaseContext context) : IProcurementOrderQueryRepository
{
    private const string Columns = """
        h.Uid AS PurchaseOrderId,h.OrderNo,h.CustomerId,h.CustomerCode,h.CustomerNameSnapshot AS CustomerName,
        h.SupplierId,h.SupplierCode,h.SupplierName,h.OrderDate,h.ConfirmationStatus,h.LifecycleStatus,h.Version
        """;
    /// <inheritdoc/>
    public async Task<PagedResponseBase<ProcurementOrderListItemDto>> SearchAsync(ProcurementOrderSearchRequest request, CancellationToken ct)
    {
        const string filter = """
            FROM dbo.PurchaseOrderHead h WHERE h.ExecutionModel=1 AND h.IsDeleted=0
            AND (@CustomerId IS NULL OR h.CustomerId=@CustomerId) AND (@SupplierId IS NULL OR h.SupplierId=@SupplierId)
            AND (@ConfirmationStatus IS NULL OR h.ConfirmationStatus=@ConfirmationStatus)
            AND (@Keyword IS NULL OR h.OrderNo LIKE @Keyword ESCAPE N'~')
            """;
        var keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null
            : "%" + request.Keyword.Trim().Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";
        using var grid = await context.Session.Connection.QueryMultipleAsync(new CommandDefinition(
            "SELECT COUNT(*) " + filter + "; SELECT " + Columns + " " + filter +
            " ORDER BY h.OrderDate DESC,h.OrderNo DESC,h.Uid DESC OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;",
            new { request.CustomerId, request.SupplierId, request.ConfirmationStatus, Keyword = keyword,
                Skip = (request.PageIndex - 1) * request.PageSize, Take = request.PageSize },
            context.Session.Transaction, cancellationToken: ct));
        var count = await grid.ReadSingleAsync<int>();
        return new() { Items = (await grid.ReadAsync<ProcurementOrderListItemDto>()).ToList(),
            TotalCount = count, PageIndex = request.PageIndex, PageSize = request.PageSize };
    }
    /// <inheritdoc/>
    public async Task<ProcurementOrderDetailDto?> GetDetailAsync(Guid uid, CancellationToken ct)
    {
        using var grid = await context.Session.Connection.QueryMultipleAsync(new CommandDefinition("""
            SELECT h.Uid AS PurchaseOrderId,h.OrderNo,h.CustomerId,h.CustomerCode,h.CustomerNameSnapshot AS CustomerName,
              h.SupplierId,h.SupplierCode,h.SupplierName,h.Currency,h.OrderDate,h.Remark,h.PackagingRequirements,
              h.Version,h.ConfirmationStatus,h.LifecycleStatus,h.ActiveConfirmationId
            FROM dbo.PurchaseOrderHead h WHERE h.Uid=@uid AND h.ExecutionModel=1 AND h.IsDeleted=0;
            SELECT l.Uid AS PurchaseOrderLineId,l.LineNumber,l.Version,l.ProductId,l.Sku,l.UnitSnapshot,
              l.QTY AS Quantity,l.UnlinkedQty,l.DeliveryWarehouseSiteId AS WarehouseSiteId,
              l.DeliveryWarehouseCodeSnapshot AS WarehouseCode,l.DeliveryWarehouseNameSnapshot AS WarehouseName,
              l.DeliveryTargetSupplierId AS DeliverySupplierId,l.DeliverySupplierCodeSnapshot AS DeliverySupplierCode,
              l.DeliverySupplierNameSnapshot AS DeliverySupplierName,l.UnitPrice,l.TaxPrice,l.UnitAmount,l.TaxAmount,
              l.PlannedDeliveryDate,l.SupplierSku,l.DescriptionZh,l.SubNote
            FROM dbo.PurchaseOrderLine l JOIN dbo.PurchaseOrderHead h ON h.Uid=l.PurchaseOrderId
            WHERE h.Uid=@uid AND h.ExecutionModel=1 AND h.IsDeleted=0 AND l.IsDeleted=0 ORDER BY l.LineNumber,l.Uid;
            """, new { uid }, context.Session.Transaction, cancellationToken: ct));
        var head = await grid.ReadSingleOrDefaultAsync<ProcurementOrderDetailDto>();
        var lines = (await grid.ReadAsync<ProcurementOrderLineDto>()).ToList();
        if (head is not null) head.Lines = lines;
        return head;
    }
}
