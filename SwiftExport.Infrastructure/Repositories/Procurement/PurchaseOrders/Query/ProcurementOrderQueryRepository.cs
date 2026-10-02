using Dapper;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Contexts;
namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Query;

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
            FROM dbo.PurchaseOrderHead h WHERE h.IsDeleted=0
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
    public async Task<PurchaseOrderDetailDto?> GetDetailAsync(Guid uid, CancellationToken ct)
    {
        using var grid = await context.Session.Connection.QueryMultipleAsync(new CommandDefinition("""
            SELECT h.Uid AS PurchaseOrderId,h.OrderNo,h.CustomerId,h.CustomerCode,h.CustomerNameSnapshot AS CustomerName,
              h.SupplierId,h.SupplierCode,h.SupplierName,h.Currency,h.DiscountRate,h.DiscountAmount,h.BoNo,h.CustomerPoNo,
              h.DemandSourceType,h.DemandSourceUid,h.DemandSourceNo,h.SoPerson AS SalesPerson,
              h.PoPerson AS PurchasePerson,h.InletrHoseType AS InletHoseType,h.AeratorType,h.PalletType,h.BoxType,h.LabelType,
              h.IMType AS InstructionManualType,h.CartonType,h.LogoType,h.ReviewedBy,h.PoFolder AS PurchaseOrderFolder,
              h.ZheKouBeiZhu AS DiscountRemark,h.OrderDate,h.ExpectedDate,h.ReplyDate,h.ConfirmedDate,h.ConfirmedBy,
              h.Remark,h.PackagingRequirements,h.Version,h.ConfirmationStatus,h.LifecycleStatus,h.ActiveConfirmationId,
              CASE WHEN h.ConfirmationStatus=@Pending AND h.LifecycleStatus=@Active THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS CanEditDraft,
              CASE WHEN h.ConfirmationStatus=@Pending AND h.LifecycleStatus=@Active THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS CanConfirm,
              CASE WHEN h.ConfirmationStatus=@Confirmed AND h.LifecycleStatus=@Active THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS CanCancelConfirmation,
              CASE WHEN h.LifecycleStatus<>@Active THEN N'采购单不是活动状态。'
                   WHEN h.ConfirmationStatus NOT IN (@Pending,@Confirmed) THEN N'采购确认状态不支持当前命令。'
                   ELSE N'' END AS CommandBlockedReason,
              totals.LineCount,totals.TotalQuantity,totals.CnyTotalAmount,
              h.CreatedDate,h.CreatedBy,h.UpdatedDate,h.UpdatedBy
            FROM dbo.PurchaseOrderHead h
            OUTER APPLY (
              SELECT COUNT(1) AS LineCount,COALESCE(SUM(l.QTY),0) AS TotalQuantity,
                COALESCE(SUM(l.UnitAmount),0) AS CnyTotalAmount
              FROM dbo.PurchaseOrderLine l WHERE l.PurchaseOrderId=h.Uid AND l.IsDeleted=0
            ) totals
            WHERE h.Uid=@uid AND h.IsDeleted=0;
            SELECT l.Uid,l.LineNumber,l.Version,l.ProductId,p.MainImage,l.Sku,l.UnitSnapshot,
              CAST(CASE WHEN l.ProductId IS NULL THEN 1 ELSE 0 END AS bit) AS IsNonStandard,
              l.CustomerSku,l.LinkSku,l.FinishZh,l.FeaturesZh,l.MaterialType,l.SupplyType,
              l.QTY AS Quantity,l.UnlinkedQty,
              CASE WHEN l.MaterialType=N'客供件' THEN l.DeliveryTargetSupplierId END AS DeliverySupplierId,
              CASE WHEN l.MaterialType=N'客供件' THEN l.DeliverySupplierCodeSnapshot ELSE N'' END AS DeliverySupplierCode,
              CASE WHEN l.MaterialType=N'客供件' THEN l.DeliverySupplierNameSnapshot ELSE N'' END AS DeliverySupplierName,
              l.SourceType,l.SourceNo,
              l.SourceUid,l.SourceLineId,l.SourceLineNumber,l.UnitPrice,l.TaxPrice,l.UnitAmount,l.TaxAmount,
              l.PlannedDeliveryDate,l.SupplierSku,l.DescriptionZh,l.SubNote,l.CreatedDate,l.CreatedBy,l.UpdatedDate,l.UpdatedBy
            FROM dbo.PurchaseOrderLine l JOIN dbo.PurchaseOrderHead h ON h.Uid=l.PurchaseOrderId
            LEFT JOIN dbo.Product p ON p.Uid=l.ProductId AND p.IsDeleted=0
            WHERE h.Uid=@uid AND h.IsDeleted=0 AND l.IsDeleted=0 ORDER BY l.LineNumber,l.Uid;
            """, new
            {
                uid,
                Pending = (int)ConfirmationStatusEnum.待确认,
                Confirmed = (int)ConfirmationStatusEnum.已确认,
                Active = (int)PurchaseOrderLifecycleStatusEnum.Active
            }, context.Session.Transaction, cancellationToken: ct));
        var head = await grid.ReadSingleOrDefaultAsync<PurchaseOrderDetailDto>();
        var lines = (await grid.ReadAsync<PurchaseOrderLineDetailDto>()).ToList();
        if (head is not null) head.Lines = lines;
        return head;
    }
}
