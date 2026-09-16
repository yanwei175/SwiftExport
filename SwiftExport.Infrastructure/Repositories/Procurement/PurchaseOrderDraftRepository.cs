using Dapper;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Infrastructure.Repositories.Procurement;

/// <summary>
/// 旧执行链采购草稿写入与生命周期专用仓储；所有读写按采购头执行模型隔离。
/// 净需求服务通过本仓储创建采购草稿，但持久化模型始终是正式采购单头和采购单行。
/// </summary>
public sealed class PurchaseOrderDraftRepository(RepositoryBaseContext context)
    : IPurchaseOrderDraftRepository
{
    private DbSession Session => context.Session;

    /// <summary>按执行请求锁定旧链采购头，禁止新链参与需求幂等范围。</summary>
    public Task<PurchaseOrderHead?> GetByExecutionScopeForUpdateAsync(
        Guid executionRequestId,
        string sourceType,
        Guid sourceUid,
        Guid supplierId)
    {
        if (executionRequestId == Guid.Empty) throw new ArgumentException("执行请求 Uid 不能为空。", nameof(executionRequestId));
        if (string.IsNullOrWhiteSpace(sourceType)) throw new ArgumentException("来源类型不能为空。", nameof(sourceType));
        if (sourceUid == Guid.Empty) throw new ArgumentException("来源 Uid 不能为空。", nameof(sourceUid));
        if (supplierId == Guid.Empty) throw new ArgumentException("供应商 Uid 不能为空。", nameof(supplierId));

        var sql = $"""
            SELECT {HeadColumns}
            FROM dbo.PurchaseOrderHead h WITH (UPDLOCK,HOLDLOCK)
            WHERE h.ExecutionRequestId=@ExecutionRequestId
              AND h.DemandSourceType=@SourceType
              AND h.DemandSourceUid=@SourceUid
              AND h.SupplierId=@SupplierId
              AND h.IsDeleted=0;
            """;
        return Session.Connection.QuerySingleOrDefaultAsync<PurchaseOrderHead>(sql, new
        {
            ExecutionRequestId = executionRequestId,
            SourceType = sourceType.Trim(),
            SourceUid = sourceUid,
            SupplierId = supplierId
        }, transaction: Session.Transaction);
    }

    /// <summary>列出旧链的安全追加候选，不向需求预览暴露新执行链。</summary>
    public async Task<IReadOnlyList<SafePurchaseDraftCandidateDto>> GetSafeAppendCandidatesAsync(
        string sourceType,
        Guid sourceUid,
        IEnumerable<Guid> supplierIds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceType)) throw new ArgumentException("来源类型不能为空。", nameof(sourceType));
        if (sourceUid == Guid.Empty) throw new ArgumentException("来源 Uid 不能为空。", nameof(sourceUid));
        var ids = supplierIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0) return [];

        var sql = $"""
            SELECT h.Uid AS PurchaseOrderId,h.Version,h.OrderNo,
                   h.DemandSourceType AS SourceType,h.DemandSourceUid AS SourceUid,h.SupplierId,
                   COALESCE(amounts.UntaxedTotalAmount,0) AS UntaxedTotalAmount,
                   COALESCE(amounts.TaxIncludedTotalAmount,0) AS TaxIncludedTotalAmount
            FROM dbo.PurchaseOrderHead h
            OUTER APPLY
            (
                SELECT SUM(COALESCE(line.UnitAmount,0)) AS UntaxedTotalAmount,
                       SUM(COALESCE(line.TaxAmount,0)) AS TaxIncludedTotalAmount
                FROM dbo.PurchaseOrderLine line
                WHERE line.PurchaseOrderId=h.Uid AND line.IsDeleted=0
            ) amounts
            WHERE h.DemandSourceType=@SourceType
              AND h.DemandSourceUid=@SourceUid
              AND h.SupplierId IN @SupplierIds
              AND h.LifecycleStatus=@ActiveLifecycle
              AND h.ConfirmationStatus=@PendingConfirmation
              AND h.IsDeleted=0
              AND {SafeFactsPredicate}
            ORDER BY h.OrderNo,h.Uid;
            """;
        var rows = await Session.Connection.QueryAsync<SafePurchaseDraftCandidateDto>(new CommandDefinition(
            sql,
            new
            {
                SourceType = sourceType.Trim(),
                SourceUid = sourceUid,
                SupplierIds = ids,
                ActiveLifecycle = PurchaseOrderLifecycleStatusEnum.Active,
                PendingConfirmation = ConfirmationStatusEnum.待确认
            },
            Session.Transaction,
            cancellationToken: cancellationToken));
        return [.. rows];
    }

    /// <summary>按版本锁定可安全追加的旧链采购草稿。</summary>
    public Task<PurchaseOrderHead?> GetSafeDraftForUpdateAsync(Guid uid, int expectedVersion)
    {
        ValidateHeadKey(uid, expectedVersion);
        var sql = $"""
            SELECT {HeadColumns}
            FROM dbo.PurchaseOrderHead h WITH (UPDLOCK,HOLDLOCK)
            WHERE h.Uid=@Uid AND h.Version=@ExpectedVersion
              AND h.LifecycleStatus=@ActiveLifecycle
              AND h.ConfirmationStatus=@PendingConfirmation
              AND h.DemandSourceType<>N''
              AND h.DemandSourceUid<>@EmptyUid
              AND h.SupplierId<>@EmptyUid
              AND h.ExecutionRequestId<>@EmptyUid
              AND h.IsDeleted=0
              AND {SafeFactsPredicateWithLocks};
            """;
        return Session.Connection.QuerySingleOrDefaultAsync<PurchaseOrderHead>(sql, new
        {
            Uid = uid,
            ExpectedVersion = expectedVersion,
            EmptyUid = Guid.Empty,
            ActiveLifecycle = PurchaseOrderLifecycleStatusEnum.Active,
            PendingConfirmation = ConfirmationStatusEnum.待确认
        }, transaction: Session.Transaction);
    }

    /// <summary>仅读取旧链采购头，供需求创建回执重建。</summary>
    public async Task<IReadOnlyList<PurchaseOrderHead>> GetPurchaseDraftsByIdsAsync(
        IEnumerable<Guid> uids,
        CancellationToken cancellationToken = default)
    {
        var ids = uids.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0) return [];
        var rows = await Session.Connection.QueryAsync<PurchaseOrderHead>(new CommandDefinition(
            $"SELECT {HeadColumns} FROM dbo.PurchaseOrderHead h WHERE h.Uid IN @Ids AND h.IsDeleted=0 ORDER BY h.DemandSourceType,h.DemandSourceUid,h.SupplierId,h.Uid;",
            new { Ids = ids },
            Session.Transaction,
            cancellationToken: cancellationToken));
        return [.. rows];
    }

    /// <summary>插入明确归属旧链的采购头，拒绝新执行模型。</summary>
    public async Task<int> InsertPurchaseDraftAsync(PurchaseOrderHead head)
    {
        ArgumentNullException.ThrowIfNull(head);
        if (head.Uid == Guid.Empty) throw new ArgumentException("采购草稿必须由 Application 预生成 Uid。", nameof(head));
        context.AuditStamp.StampForInsert(head);
        head.IsDeleted = false;
        if (head.Version <= 0) head.Version = 1;

        const string sql = """
            INSERT INTO dbo.PurchaseOrderHead
            (Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,
             OriginKind,ConfirmationStatus,LifecycleStatus,OrderNo,BoNo,CustomerPoNo,CustomerId,CustomerCode,CustomerNameSnapshot,Currency,DiscountRate,DiscountAmount,
             SupplierId,SupplierCode,SupplierName,DemandSourceType,DemandSourceUid,DemandSourceNo,
             ExecutionRequestId,SoPerson,PoPerson,InletrHoseType,AeratorType,PalletType,BoxType,
             LabelType,IMType,CartonType,LogoType,ReviewedBy,PoFolder,ZheKouBeiZhu,Remark,
             OrderDate,ExpectedDate,ReplyDate,ConfirmedDate,ConfirmedBy)
            VALUES
            (@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,
             @OriginKind,@ConfirmationStatus,@LifecycleStatus,@OrderNo,@BoNo,@CustomerPoNo,@CustomerId,@CustomerCode,@CustomerNameSnapshot,@Currency,@DiscountRate,@DiscountAmount,
             @SupplierId,@SupplierCode,@SupplierName,@DemandSourceType,@DemandSourceUid,@DemandSourceNo,
             @ExecutionRequestId,@SoPerson,@PoPerson,@InletrHoseType,@AeratorType,@PalletType,@BoxType,
             @LabelType,@IMType,@CartonType,@LogoType,@ReviewedBy,@PoFolder,@ZheKouBeiZhu,@Remark,
             @OrderDate,@ExpectedDate,@ReplyDate,@ConfirmedDate,@ConfirmedBy);
            """;
        return await Session.Connection.ExecuteAsync(sql, head, transaction: Session.Transaction);
    }

    /// <summary>锁定旧链采购行并取得最大行号，供安全追加。</summary>
    public Task<int> GetMaxLineNumberForUpdateAsync(Guid purchaseOrderId)
    {
        if (purchaseOrderId == Guid.Empty) throw new ArgumentException("采购头 Uid 不能为空。", nameof(purchaseOrderId));
        return Session.Connection.ExecuteScalarAsync<int>("""
            SELECT COALESCE(MAX(LineNumber),0)
            FROM dbo.PurchaseOrderLine WITH (UPDLOCK,HOLDLOCK)
            WHERE PurchaseOrderId=@PurchaseOrderId AND IsDeleted=0
              AND EXISTS(SELECT 1 FROM dbo.PurchaseOrderHead owner WHERE owner.Uid=@PurchaseOrderId AND owner.IsDeleted=0);
            """, new { PurchaseOrderId = purchaseOrderId }, transaction: Session.Transaction);
    }

    /// <summary>追加旧链采购行；在同一事务内再次锁定并校验所属采购头。</summary>
    public async Task<int> InsertPurchaseDraftLinesAsync(IEnumerable<PurchaseOrderLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var materialized = lines.ToArray();
        if (materialized.Length == 0) return 0;
        if (materialized.Any(x => x is null || x.Uid == Guid.Empty))
            throw new ArgumentException("采购草稿行必须由 Application 预生成 Uid。", nameof(lines));

        foreach (var line in materialized)
        {
            context.AuditStamp.StampForInsert(line);
            line.IsDeleted = false;
            if (line.Version <= 0) line.Version = 1;
        }

        const string sql = """
            IF NOT EXISTS(SELECT 1 FROM dbo.PurchaseOrderHead owner WITH (UPDLOCK,HOLDLOCK)
                          WHERE owner.Uid=@PurchaseOrderId AND owner.IsDeleted=0)
                THROW 51000,N'采购单不存在或已删除，不能追加采购行。',1;
            INSERT INTO dbo.PurchaseOrderLine
            (Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,
             PurchaseOrderId,LineNumber,OrderNo,ProductId,Sku,CustomerSku,LinkSku,SupplierSku,
             DescriptionZh,FinishZh,FeaturesZh,UnitSnapshot,QTY,UnlinkedQty,UnitPrice,TaxPrice,UnitAmount,TaxAmount,
             PlannedDeliveryDate,DeliveryTargetSupplierId,
             DeliverySupplierCodeSnapshot,DeliverySupplierNameSnapshot,SubNote,SupplyType,MaterialType,
             SourceType,SourceNo,SourceUid,SourceLineId,SourceLineNumber)
            VALUES
            (@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,
             @PurchaseOrderId,@LineNumber,@OrderNo,@ProductId,@Sku,@CustomerSku,@LinkSku,@SupplierSku,
             @DescriptionZh,@FinishZh,@FeaturesZh,@UnitSnapshot,@Quantity,@UnlinkedQty,@UnitPrice,@TaxPrice,@UnitAmount,@TaxAmount,
             @PlannedDeliveryDate,@DeliveryTargetSupplierId,
             @DeliverySupplierCodeSnapshot,@DeliverySupplierNameSnapshot,@SubNote,@SupplyType,@MaterialType,
             @SourceType,@SourceNo,@SourceUid,@SourceLineId,@SourceLineNumber);
            """;
        return await Session.Connection.ExecuteAsync(sql, materialized, transaction: Session.Transaction);
    }

    public Task<int> AdvanceDraftVersionAfterAppendAsync(Guid uid, int expectedVersion)
        => AdvanceDraftVersionAsync(uid, expectedVersion);

    /// <summary>追加已有草稿后推进采购头版本并重算交期，始终保留下游事实门禁。</summary>
    private Task<int> AdvanceDraftVersionAsync(Guid uid, int expectedVersion)
    {
        ValidateHeadKey(uid, expectedVersion);
        var sql = $"""
            UPDATE h
               SET ExpectedDate=facts.EarliestPlannedDeliveryDate,
                   UpdatedDate=@Now,UpdatedBy=@UserName,Version=h.Version+1
            FROM dbo.PurchaseOrderHead h
            CROSS APPLY
            (
                SELECT MIN(l.PlannedDeliveryDate) AS EarliestPlannedDeliveryDate
                FROM dbo.PurchaseOrderLine l WITH (UPDLOCK,HOLDLOCK)
                WHERE l.PurchaseOrderId=h.Uid AND l.IsDeleted=0 AND l.QTY>0
            ) facts
            WHERE h.Uid=@Uid AND h.Version=@ExpectedVersion AND h.LifecycleStatus=@Active
              AND h.ConfirmationStatus=@Pending AND h.IsDeleted=0
              AND {SafeFactsPredicateWithLocks};
            """;
        return Session.Connection.ExecuteAsync(sql, new
        {
            Uid = uid,
            ExpectedVersion = expectedVersion,
            Active = PurchaseOrderLifecycleStatusEnum.Active,
            Pending = ConfirmationStatusEnum.待确认,
            Now = DateTime.Now,
            UserName = context.CurrentUser.UserName
        }, transaction: Session.Transaction);
    }

    private static void ValidateHeadKey(Guid uid, int expectedVersion)
    {
        if (uid == Guid.Empty) throw new ArgumentException("采购头 Uid 不能为空。", nameof(uid));
        if (expectedVersion <= 0) throw new ArgumentOutOfRangeException(nameof(expectedVersion));
    }

    private const string HeadColumns = """
        h.Uid,h.Version,h.IsDeleted,h.CreatedDate,h.CreatedBy,h.UpdatedDate,h.UpdatedBy,
        h.ConfirmationStatus,h.LifecycleStatus,h.OrderNo,h.BoNo,h.CustomerPoNo,h.CustomerId,h.CustomerCode,h.CustomerNameSnapshot,h.Currency,h.DiscountRate,h.DiscountAmount,
        h.SupplierId,h.SupplierCode,h.SupplierName,h.DemandSourceType,h.DemandSourceUid,h.DemandSourceNo,
        h.ExecutionRequestId,h.SoPerson,h.PoPerson,h.InletrHoseType,h.AeratorType,h.PalletType,h.BoxType,
        h.LabelType,h.IMType,h.CartonType,h.LogoType,h.ReviewedBy,h.PoFolder,h.ZheKouBeiZhu,h.Remark,
        h.OrderDate,h.ExpectedDate,h.ReplyDate,h.ConfirmedDate,h.ConfirmedBy
        """;

    // 入库来源直接指向采购行；包含软删采购行，避免历史收货或库存事实被漏掉。
    private const string SafeFactsPredicate = """
        NOT EXISTS
        (
            SELECT 1 FROM dbo.InboundDocumentLine receiveFact
            WHERE ISNULL(receiveFact.IsDeleted,0)=0
              AND receiveFact.SourceLineId IN
                   (SELECT purchaseLine.Uid FROM dbo.PurchaseOrderLine purchaseLine
                    WHERE purchaseLine.PurchaseOrderId=h.Uid)
        )
        AND NOT EXISTS
        (
            SELECT 1 FROM dbo.InventoryLedgerEntry inventoryFact
            JOIN dbo.InboundDocumentLine receiveFact ON receiveFact.Uid=inventoryFact.SourceLineId
            WHERE receiveFact.SourceLineId IN
                  (SELECT purchaseLine.Uid FROM dbo.PurchaseOrderLine purchaseLine
                   WHERE purchaseLine.PurchaseOrderId=h.Uid)
        )
        """;

    private const string SafeFactsPredicateWithLocks = """
        NOT EXISTS
        (
            SELECT 1 FROM dbo.InboundDocumentLine receiveFact WITH (UPDLOCK,HOLDLOCK)
            WHERE ISNULL(receiveFact.IsDeleted,0)=0
              AND receiveFact.SourceLineId IN
                   (SELECT purchaseLine.Uid FROM dbo.PurchaseOrderLine purchaseLine WITH (UPDLOCK,HOLDLOCK)
                    WHERE purchaseLine.PurchaseOrderId=h.Uid)
        )
        AND NOT EXISTS
        (
            SELECT 1 FROM dbo.InventoryLedgerEntry inventoryFact WITH (UPDLOCK,HOLDLOCK)
            JOIN dbo.InboundDocumentLine receiveFact WITH (UPDLOCK,HOLDLOCK) ON receiveFact.Uid=inventoryFact.SourceLineId
            WHERE receiveFact.SourceLineId IN
                  (SELECT purchaseLine.Uid FROM dbo.PurchaseOrderLine purchaseLine WITH (UPDLOCK,HOLDLOCK)
                   WHERE purchaseLine.PurchaseOrderId=h.Uid)
        )
        """;
}
