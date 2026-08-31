using Dapper;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Contexts;
namespace SwiftExport.Infrastructure.Repositories.Procurement;

/// <summary>新采购事务仓储。所有读取参与调用方事务，锁定资格和行集合，禁止读取需求贡献。</summary>
public sealed class ProcurementOrderRepository(RepositoryBaseContext context) : IProcurementOrderRepository
{
    private CommandDefinition Command(string sql, object? args, CancellationToken ct)
        => new(sql, args, context.Session.Transaction ?? throw new InvalidOperationException("采购持久化需要事务。"), cancellationToken: ct);

    /// <inheritdoc/>
    public async Task<bool> HasPermissionAsync(Guid actorUid, string operation, CancellationToken ct)
        => await context.Session.Connection.ExecuteScalarAsync<int>(Command("""
            SELECT COUNT(*) FROM dbo.SysUser u WITH(HOLDLOCK)
            JOIN dbo.SysUser_Roles ur WITH(HOLDLOCK) ON ur.UserId=u.Uid AND ISNULL(ur.IsDeleted,0)=0
            JOIN dbo.SysRoles r WITH(HOLDLOCK) ON r.Uid=ur.RoleId AND ISNULL(r.IsDeleted,0)=0
            WHERE u.Uid=@actorUid AND u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0
              AND r.RoleName IN(N'Admin',@role);
            """, new { actorUid, role = "Procurement.Order." + operation }, ct)) > 0;
    /// <inheritdoc/>
    public Task<ProcurementCommandReceipt?> GetReceiptAsync(Guid commandId, CancellationToken ct)
        => context.Session.Connection.QuerySingleOrDefaultAsync<ProcurementCommandReceipt>(Command(
            "SELECT * FROM dbo.ProcurementCommandReceipt WITH(UPDLOCK,HOLDLOCK) WHERE CommandId=@commandId;", new { commandId }, ct));
    /// <inheritdoc/>
    public Task<PurchaseOrderHead?> GetHeadAsync(Guid uid, CancellationToken ct)
        => context.Session.Connection.QuerySingleOrDefaultAsync<PurchaseOrderHead>(Command(
            "SELECT * FROM dbo.PurchaseOrderHead WITH(UPDLOCK,HOLDLOCK) WHERE Uid=@uid AND ExecutionModel=1 AND IsDeleted=0;", new { uid }, ct));
    /// <inheritdoc/>
    public async Task<List<PurchaseOrderLine>> GetLinesAsync(Guid uid, CancellationToken ct)
        => (await context.Session.Connection.QueryAsync<PurchaseOrderLine>(Command("""
            SELECT l.*,l.QTY AS Quantity FROM dbo.PurchaseOrderLine l WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.PurchaseOrderHead h WITH(HOLDLOCK) ON h.Uid=l.PurchaseOrderId AND h.ExecutionModel=1 AND h.IsDeleted=0
            WHERE l.PurchaseOrderId=@uid AND l.IsDeleted=0 ORDER BY l.LineNumber,l.Uid;
            """, new { uid }, ct))).ToList();
    /// <inheritdoc/>
    public Task<Customer?> GetCustomerAsync(Guid uid, CancellationToken ct)
        => context.Session.Connection.QuerySingleOrDefaultAsync<Customer>(Command(
            "SELECT Uid,Version,IsActive,CustomerCode,CustomerName FROM dbo.Customer WITH(HOLDLOCK) WHERE Uid=@uid AND ISNULL(IsDeleted,0)=0;", new { uid }, ct));
    /// <inheritdoc/>
    public Task<Supplier?> GetSupplierAsync(Guid uid, CancellationToken ct)
        => context.Session.Connection.QuerySingleOrDefaultAsync<Supplier>(Command(
            "SELECT Uid,Version,IsActive,SupplierCode,SupplierName FROM dbo.Supplier WITH(HOLDLOCK) WHERE Uid=@uid AND ISNULL(IsDeleted,0)=0;", new { uid }, ct));
    /// <inheritdoc/>
    public Task<Product?> GetProductAsync(Guid uid, CancellationToken ct)
        => context.Session.Connection.QuerySingleOrDefaultAsync<Product>(Command(
            "SELECT Uid,Version,IsActive,Sku,DescriptionZh,Unit,MaterialType,SupplyType,BomNo,BomVersion FROM dbo.Product WITH(HOLDLOCK) WHERE Uid=@uid AND ISNULL(IsDeleted,0)=0;", new { uid }, ct));
    /// <inheritdoc/>
    public Task<WarehouseSite?> GetWarehouseAsync(Guid uid, CancellationToken ct)
        => context.Session.Connection.QuerySingleOrDefaultAsync<WarehouseSite>(Command(
            "SELECT * FROM dbo.WarehouseSite WITH(HOLDLOCK) WHERE Uid=@uid AND IsDeleted=0;", new { uid }, ct));
    /// <inheritdoc/>
    public async Task<string?> GetUnitAsync(string unit, CancellationToken ct)
    {
        var values = (await context.Session.Connection.QueryAsync<string>(Command("""
            SELECT LTRIM(RTRIM(DataNameEn)) FROM dbo.SysDictData WITH(HOLDLOCK)
            WHERE DictCode=N'Unit' AND IsEnabled=1 AND ISNULL(IsDeleted,0)=0
              AND UPPER(LTRIM(RTRIM(DataNameEn)))=UPPER(@unit);
            """, new { unit }, ct))).ToList();
        return values.Count == 1 ? values[0] : null;
    }
    /// <inheritdoc/>
    public async Task InsertAsync(PurchaseOrderHead head, IReadOnlyList<PurchaseOrderLine> lines, CancellationToken ct)
    {
        context.AuditStamp.StampForInsert(head);
        await context.Session.Connection.ExecuteAsync(Command("""
            INSERT dbo.PurchaseOrderHead
            (Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,ExecutionModel,OriginKind,
             CustomerId,CustomerCode,CustomerNameSnapshot,SupplierId,SupplierCode,SupplierName,OrderNo,Currency,
             OrderDate,ExpectedDate,Remark,PackagingRequirements,ExecutionRequestId,DemandSourceType,DemandSourceUid,DemandSourceNo,
             ConfirmationStatus,LifecycleStatus)
            VALUES
            (@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@ExecutionModel,@OriginKind,
             @CustomerId,@CustomerCode,@CustomerNameSnapshot,@SupplierId,@SupplierCode,@SupplierName,@OrderNo,@Currency,
             @OrderDate,@ExpectedDate,@Remark,@PackagingRequirements,@ExecutionRequestId,NULL,NULL,NULL,@ConfirmationStatus,@LifecycleStatus);
            """, head, ct));
        foreach (var line in lines)
        {
            context.AuditStamp.StampForInsert(line);
            await context.Session.Connection.ExecuteAsync(Command("""
                INSERT dbo.PurchaseOrderLine
                (Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,PurchaseOrderId,LineNumber,OrderNo,
                 ProductId,Sku,UnitSnapshot,QTY,UnlinkedQty,SupplyType,MaterialType,DeliveryWarehouseSiteId,
                 DeliveryWarehouseCodeSnapshot,DeliveryWarehouseNameSnapshot,DeliveryTargetSupplierId,
                 DeliverySupplierCodeSnapshot,DeliverySupplierNameSnapshot,UnitPrice,TaxPrice,UnitAmount,TaxAmount,
                 PlannedDeliveryDate,SupplierSku,DescriptionZh,SubNote,SourceType,SourceNo)
                VALUES
                (@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@PurchaseOrderId,@LineNumber,@OrderNo,
                 @ProductId,@Sku,@UnitSnapshot,@Quantity,@UnlinkedQty,@SupplyType,@MaterialType,@DeliveryWarehouseSiteId,
                 @DeliveryWarehouseCodeSnapshot,@DeliveryWarehouseNameSnapshot,@DeliveryTargetSupplierId,
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
            WHERE Uid=@Uid AND Version=@Version AND ExecutionModel=1 AND ConfirmationStatus=0 AND LifecycleStatus=0 AND IsDeleted=0;
            """, head, ct)) != 1) throw new InvalidOperationException("采购头并发冲突。");
        foreach (var line in lines)
        {
            context.AuditStamp.StampForUpdate(line);
            if (await context.Session.Connection.ExecuteAsync(Command("""
                UPDATE l SET SupplierSku=@SupplierSku,DescriptionZh=@DescriptionZh,SubNote=@SubNote,
                  UnitPrice=@UnitPrice,TaxPrice=@TaxPrice,UnitAmount=@UnitAmount,TaxAmount=@TaxAmount,
                  PlannedDeliveryDate=@PlannedDeliveryDate,Version=l.Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
                FROM dbo.PurchaseOrderLine l JOIN dbo.PurchaseOrderHead h ON h.Uid=l.PurchaseOrderId
                WHERE l.Uid=@Uid AND l.PurchaseOrderId=@PurchaseOrderId AND l.Version=@Version AND l.IsDeleted=0
                  AND h.ExecutionModel=1 AND h.IsDeleted=0 AND h.ConfirmationStatus=0 AND h.LifecycleStatus=0;
                """, line, ct)) != 1) throw new InvalidOperationException("采购行并发冲突。");
        }
    }
    /// <inheritdoc/>
    public async Task ConfirmAsync(PurchaseOrderHead head, IReadOnlyList<PurchaseConfirmationLine> snapshots, IReadOnlyList<PurchaseConfirmationNode> nodes, CancellationToken ct)
    {
        foreach (var item in snapshots)
        {
            context.AuditStamp.StampForInsert(item);
            await context.Session.Connection.ExecuteAsync(Command("""
                INSERT dbo.PurchaseConfirmationLine
                (Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,ConfirmationId,PurchaseOrderId,
                 PurchaseOrderLineId,CommandId,RootProductId,LineVersion,HasBom,Fingerprint,ConfirmedAt,RatioPolicy)
                VALUES
                (@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@ConfirmationId,@PurchaseOrderId,
                 @PurchaseOrderLineId,@CommandId,@RootProductId,@LineVersion,@HasBom,@Fingerprint,@ConfirmedAt,@RatioPolicy);
                """, item, ct));
        }
        foreach (var item in nodes)
        {
            context.AuditStamp.StampForInsert(item);
            await context.Session.Connection.ExecuteAsync(Command("""
                INSERT dbo.PurchaseConfirmationNode
                (Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,ConfirmationLineId,Path,
                 RootProductId,ParentProductId,ComponentProductId,BomLineId,ParentBomNo,ParentBomVersion,
                 ParentSkuSnapshot,ComponentSkuSnapshot,UnitSnapshot,MaterialType,SupplyType,Numerator,Denominator,
                 CumulativeRatio,IsCustomerSupplied,ConsumptionWarehouseSiteId,WarehouseCodeSnapshot,WarehouseNameSnapshot,
                 ConsumptionSupplierId,SupplierCodeSnapshot,SupplierNameSnapshot)
                VALUES
                (@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@ConfirmationLineId,@Path,
                 @RootProductId,@ParentProductId,@ComponentProductId,@BomLineId,@ParentBomNo,@ParentBomVersion,
                 @ParentSkuSnapshot,@ComponentSkuSnapshot,@UnitSnapshot,@MaterialType,@SupplyType,@Numerator,@Denominator,
                 @CumulativeRatio,@IsCustomerSupplied,@ConsumptionWarehouseSiteId,@WarehouseCodeSnapshot,@WarehouseNameSnapshot,
                 @ConsumptionSupplierId,@SupplierCodeSnapshot,@SupplierNameSnapshot);
                """, item, ct));
        }
        context.AuditStamp.StampForUpdate(head);
        if (await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.PurchaseOrderHead SET ConfirmationStatus=1,ActiveConfirmationId=@ActiveConfirmationId,
              ConfirmedDate=@ConfirmedDate,ConfirmedBy=@ConfirmedBy,Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
            WHERE Uid=@Uid AND Version=@Version AND ExecutionModel=1 AND OriginKind=2
              AND ConfirmationStatus=0 AND LifecycleStatus=0 AND IsDeleted=0;
            """, head, ct)) != 1) throw new InvalidOperationException("采购确认并发冲突。");
    }
    /// <inheritdoc/>
    public async Task InsertReceiptAsync(ProcurementCommandReceipt receipt, CancellationToken ct)
    {
        context.AuditStamp.StampForInsert(receipt);
        await context.Session.Connection.ExecuteAsync(Command("""
            INSERT dbo.ProcurementCommandReceipt
            (Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,CommandId,ActorUid,PurchaseOrderId,Operation,RequestFingerprint,ResultJson)
            VALUES(@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@CommandId,@ActorUid,@PurchaseOrderId,@Operation,@RequestFingerprint,@ResultJson);
            """, receipt, ct));
    }
}
