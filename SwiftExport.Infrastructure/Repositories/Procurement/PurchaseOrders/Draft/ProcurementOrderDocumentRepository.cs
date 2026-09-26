using Dapper;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Contexts;
namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Draft;

/// <summary>采购订单头行事务仓储；保持原锁提示、版本条件与审计打戳。</summary>
public sealed class ProcurementOrderDocumentRepository(RepositoryBaseContext context) : IProcurementOrderDocumentRepository
{
    private CommandDefinition Command(string sql, object? args, CancellationToken ct)
        => new(sql, args, context.Session.Transaction ?? throw new InvalidOperationException("采购持久化需要事务。"), cancellationToken: ct);

    /// <inheritdoc/>
    public Task<PurchaseOrderHead?> GetHeadAsync(Guid uid, CancellationToken ct)
        => context.Session.Connection.QuerySingleOrDefaultAsync<PurchaseOrderHead>(Command(
            "SELECT * FROM dbo.PurchaseOrderHead WITH(UPDLOCK,HOLDLOCK) WHERE Uid=@uid AND IsDeleted=0;", new { uid }, ct));
    /// <inheritdoc/>
    public async Task<List<PurchaseOrderLine>> GetLinesAsync(Guid uid, CancellationToken ct)
        => (await context.Session.Connection.QueryAsync<PurchaseOrderLine>(Command("""
            SELECT l.*,l.QTY AS Quantity FROM dbo.PurchaseOrderLine l WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.PurchaseOrderHead h WITH(HOLDLOCK) ON h.Uid=l.PurchaseOrderId AND h.IsDeleted=0
            WHERE l.PurchaseOrderId=@uid AND l.IsDeleted=0 ORDER BY l.LineNumber,l.Uid;
            """, new { uid }, ct))).ToList();
    /// <inheritdoc/>
    public async Task InsertAsync(PurchaseOrderHead head, IReadOnlyList<PurchaseOrderLine> lines, CancellationToken ct)
    {
        context.AuditStamp.StampForInsert(head);
        await context.Session.Connection.ExecuteAsync(Command("""
            INSERT dbo.PurchaseOrderHead
            (Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,OriginKind,
             CustomerId,CustomerCode,CustomerNameSnapshot,SupplierId,SupplierCode,SupplierName,OrderNo,Currency,DiscountRate,DiscountAmount,
             OrderDate,ExpectedDate,Remark,PackagingRequirements,ExecutionRequestId,DemandSourceType,DemandSourceUid,DemandSourceNo,
             ConfirmationStatus,LifecycleStatus)
            VALUES
            (@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@OriginKind,
             @CustomerId,@CustomerCode,@CustomerNameSnapshot,@SupplierId,@SupplierCode,@SupplierName,@OrderNo,@Currency,@DiscountRate,@DiscountAmount,
             @OrderDate,@ExpectedDate,@Remark,@PackagingRequirements,@ExecutionRequestId,NULL,NULL,NULL,@ConfirmationStatus,@LifecycleStatus);
            """, head, ct));
        foreach (var line in lines)
        {
            context.AuditStamp.StampForInsert(line);
            await context.Session.Connection.ExecuteAsync(Command("""
                INSERT dbo.PurchaseOrderLine
                (Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,PurchaseOrderId,LineNumber,OrderNo,
                 ProductId,Sku,UnitSnapshot,QTY,UnlinkedQty,SupplyType,MaterialType,DeliveryTargetSupplierId,
                 DeliverySupplierCodeSnapshot,DeliverySupplierNameSnapshot,UnitPrice,TaxPrice,UnitAmount,TaxAmount,
                 PlannedDeliveryDate,SupplierSku,DescriptionZh,SubNote,SourceType,SourceNo)
                VALUES
                (@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@PurchaseOrderId,@LineNumber,@OrderNo,
                 @ProductId,@Sku,@UnitSnapshot,@Quantity,@UnlinkedQty,@SupplyType,@MaterialType,@DeliveryTargetSupplierId,
                 @DeliverySupplierCodeSnapshot,@DeliverySupplierNameSnapshot,@UnitPrice,@TaxPrice,@UnitAmount,@TaxAmount,
                 @PlannedDeliveryDate,@SupplierSku,@DescriptionZh,@SubNote,NULL,NULL);
                """, line, ct));
        }
    }
    /// <inheritdoc/>
    public async Task SaveAsync(PurchaseOrderHead head, IReadOnlyList<PurchaseOrderLine> lines, CancellationToken ct)
    {
        context.AuditStamp.StampForUpdate(head);
        if (await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.PurchaseOrderHead SET Remark=@Remark,PackagingRequirements=@PackagingRequirements,
              ExpectedDate=@ExpectedDate,Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
            WHERE Uid=@Uid AND Version=@Version AND ConfirmationStatus=0 AND LifecycleStatus=0 AND IsDeleted=0;
            """, head, ct)) != 1) throw new ProcurementConcurrencyException("采购头并发冲突。");
        foreach (var line in lines)
        {
            context.AuditStamp.StampForUpdate(line);
            if (await context.Session.Connection.ExecuteAsync(Command("""
                UPDATE l SET SupplierSku=@SupplierSku,DescriptionZh=@DescriptionZh,SubNote=@SubNote,
                  UnitPrice=@UnitPrice,TaxPrice=@TaxPrice,UnitAmount=@UnitAmount,TaxAmount=@TaxAmount,
                  PlannedDeliveryDate=@PlannedDeliveryDate,Version=l.Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
                FROM dbo.PurchaseOrderLine l JOIN dbo.PurchaseOrderHead h ON h.Uid=l.PurchaseOrderId
                WHERE l.Uid=@Uid AND l.PurchaseOrderId=@PurchaseOrderId AND l.Version=@Version AND l.IsDeleted=0
                  AND h.IsDeleted=0 AND h.ConfirmationStatus=0 AND h.LifecycleStatus=0;
                """, line, ct)) != 1) throw new ProcurementConcurrencyException("采购行并发冲突。");
        }
    }
    /// <inheritdoc/>
    public async Task ConfirmAsync(PurchaseOrderHead head, CancellationToken ct)
    {
        context.AuditStamp.StampForUpdate(head);
        if (await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.PurchaseOrderHead SET ConfirmationStatus=1,ActiveConfirmationId=@ActiveConfirmationId,
              ConfirmedDate=@ConfirmedDate,ConfirmedBy=@ConfirmedBy,Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
            WHERE Uid=@Uid AND Version=@Version
              AND ConfirmationStatus=0 AND LifecycleStatus=0 AND IsDeleted=0;
            """, head, ct)) != 1) throw new ProcurementConcurrencyException("采购确认并发冲突。");
    }
}
