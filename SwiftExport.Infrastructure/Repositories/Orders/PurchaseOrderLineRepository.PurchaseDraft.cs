using Dapper;
using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Infrastructure.Repositories.Orders;

public partial class PurchaseOrderLineRepository
{
    public Task<int> GetMaxLineNumberForUpdateAsync(Guid purchaseOrderId)
    {
        if (purchaseOrderId == Guid.Empty)
            throw new ArgumentException("采购头 Uid 不能为空。", nameof(purchaseOrderId));

        const string sql = """
            SELECT COALESCE(MAX(LineNumber),0)
            FROM dbo.PurchaseOrderLine WITH (UPDLOCK, HOLDLOCK)
            WHERE PurchaseOrderId = @PurchaseOrderId AND IsDeleted = 0;
            """;
        return Connection.ExecuteScalarAsync<int>(
            sql, new { PurchaseOrderId = purchaseOrderId }, transaction: Transaction);
    }

    public async Task<int> InsertPurchaseDraftLinesAsync(IEnumerable<PurchaseOrderLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var materialized = lines.ToArray();
        if (materialized.Length == 0) return 0;
        if (materialized.Any(x => x is null || x.Uid == Guid.Empty))
            throw new ArgumentException("采购草稿行必须由 Application 预生成 Uid。", nameof(lines));

        foreach (var line in materialized)
        {
            AuditStamp.StampForInsert(line);
            line.IsDeleted = false;
            if (line.Version <= 0) line.Version = 1;
        }

        const string sql = """
            INSERT INTO dbo.PurchaseOrderLine
            (Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,
             ReadyStatus,LineNumber,PurchaseOrderId,OrderNo,ProductId,Sku,CustomerSku,LinkSku,SupplierSku,
             DescriptionZh,FinishZh,FeaturesZh,UnitPrice,TaxPrice,QTY,UnitAmount,TaxAmount,
             PlannedDeliveryDate,DeliveryTargetAllocationId,DeliveryTargetSupplierId,
             DeliverySupplierCodeSnapshot,DeliverySupplierNameSnapshot,SubNote,SupplyType,MaterialType,DeliveryTo,
             SourceType,SourceNo,SourceUid,SourceLineId,SourceLineNumber,ReadyQty,RecQty,LastRecDate,
             FollowUpRemarks,FollowUpTime,FollowExpectedDate)
            VALUES
            (@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,
             @ReadyStatus,@LineNumber,@PurchaseOrderId,@OrderNo,@ProductId,@Sku,@CustomerSku,@LinkSku,@SupplierSku,
             @DescriptionZh,@FinishZh,@FeaturesZh,@UnitPrice,@TaxPrice,@QTY,@UnitAmount,@TaxAmount,
             @PlannedDeliveryDate,@DeliveryTargetAllocationId,@DeliveryTargetSupplierId,
             @DeliverySupplierCodeSnapshot,@DeliverySupplierNameSnapshot,@SubNote,@SupplyType,@MaterialType,@DeliveryTo,
             @SourceType,@SourceNo,@SourceUid,@SourceLineId,@SourceLineNumber,@ReadyQty,@RecQty,@LastRecDate,
             @FollowUpRemarks,@FollowUpTime,@FollowExpectedDate);
            """;
        return await Connection.ExecuteAsync(sql, materialized, transaction: Transaction);
    }

    public async Task<IReadOnlyList<PurchaseOrderLine>> GetDraftLinesForUpdateAsync(Guid purchaseOrderId)
    {
        if (purchaseOrderId == Guid.Empty)
            throw new ArgumentException("采购头 Uid 不能为空。", nameof(purchaseOrderId));
        var rows = await Connection.QueryAsync<PurchaseOrderLine>("""
            SELECT * FROM dbo.PurchaseOrderLine WITH (UPDLOCK,HOLDLOCK)
            WHERE PurchaseOrderId=@PurchaseOrderId AND IsDeleted=0
            ORDER BY LineNumber,Uid;
            """, new { PurchaseOrderId = purchaseOrderId }, transaction: Transaction);
        return [.. rows];
    }

    public async Task<int> UpdateDraftLineQuantitiesAsync(
        IReadOnlyDictionary<Guid, decimal> quantities,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(quantities);
        if (quantities.Count == 0) return 0;
        var affected = 0;
        const string sql = """
            UPDATE dbo.PurchaseOrderLine
            SET QTY=@Qty,UnitAmount=COALESCE(UnitPrice,0)*@Qty,TaxAmount=COALESCE(TaxPrice,0)*@Qty,
                SubNote=CASE WHEN NULLIF(@Reason,N'') IS NULL THEN SubNote
                             WHEN NULLIF(SubNote,N'') IS NULL THEN @Reason
                             ELSE CONCAT(SubNote,N'；',@Reason) END,
                UpdatedDate=@Now,UpdatedBy=@UserName,Version=Version+1
            WHERE Uid=@Uid AND IsDeleted=0;
            """;
        foreach (var item in quantities)
        {
            affected += await Connection.ExecuteAsync(sql, new
            {
                Uid = item.Key,
                Qty = item.Value,
                Reason = reason,
                Now = DateTime.Now,
                UserName = CurrentUser.UserName,
            }, transaction: Transaction);
        }
        return affected;
    }
}
