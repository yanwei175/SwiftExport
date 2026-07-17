using Dapper;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Infrastructure.Repositories.DemandPlanning;

/// <summary>净需求采购草稿专用仓储；只暴露当前计划链路需要的头行能力。</summary>
public sealed class NetDemandPurchaseDraftRepository(RepositoryBaseContext context)
    : INetDemandPurchaseDraftRepository
{
    private DbSession Session => context.Session;

    public Task<NetDemandPurchaseDraftHead?> GetByExecutionScopeForUpdateAsync(
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
            SELECT {PurchaseDraftHeadColumns}
            FROM dbo.PurchaseOrderHead h WITH (UPDLOCK,HOLDLOCK)
            WHERE h.ExecutionRequestId=@ExecutionRequestId
              AND h.DemandSourceType=@SourceType
              AND h.DemandSourceUid=@SourceUid
              AND h.SupplierId=@SupplierId
              AND h.IsDeleted=0;
            """;
        return Session.Connection.QuerySingleOrDefaultAsync<NetDemandPurchaseDraftHead>(sql, new
        {
            ExecutionRequestId = executionRequestId,
            SourceType = sourceType.Trim(),
            SourceUid = sourceUid,
            SupplierId = supplierId
        }, transaction: Session.Transaction);
    }

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
                   h.DemandSourceType AS SourceType,h.DemandSourceUid AS SourceUid,h.SupplierId
            FROM dbo.PurchaseOrderHead h
            WHERE h.DemandSourceType=@SourceType
              AND h.DemandSourceUid=@SourceUid
              AND h.SupplierId IN @SupplierIds
              AND h.LifecycleStatus=@ActiveLifecycle
              AND h.ConfirmationStatus=@PendingConfirmation
              AND h.IsDeleted=0
              AND {SafeAppendFactsPredicate}
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

    public Task<NetDemandPurchaseDraftHead?> GetSafeDraftForUpdateAsync(Guid uid, int expectedVersion)
    {
        if (uid == Guid.Empty) throw new ArgumentException("采购头 Uid 不能为空。", nameof(uid));
        if (expectedVersion <= 0) throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        var sql = $"""
            SELECT {PurchaseDraftHeadColumns}
            FROM dbo.PurchaseOrderHead h WITH (UPDLOCK,HOLDLOCK)
            WHERE h.Uid=@Uid AND h.Version=@ExpectedVersion
              AND h.LifecycleStatus=@ActiveLifecycle
              AND h.ConfirmationStatus=@PendingConfirmation
              AND h.DemandSourceType IS NOT NULL
              AND h.DemandSourceUid IS NOT NULL
              AND h.SupplierId IS NOT NULL
              AND h.IsDeleted=0
              AND {SafeAppendFactsPredicateWithLocks};
            """;
        return Session.Connection.QuerySingleOrDefaultAsync<NetDemandPurchaseDraftHead>(sql, new
        {
            Uid = uid,
            ExpectedVersion = expectedVersion,
            ActiveLifecycle = PurchaseOrderLifecycleStatusEnum.Active,
            PendingConfirmation = ConfirmationStatusEnum.待确认
        }, transaction: Session.Transaction);
    }

    public async Task<IReadOnlyList<NetDemandPurchaseDraftHead>> GetPurchaseDraftsByIdsAsync(
        IEnumerable<Guid> uids,
        CancellationToken cancellationToken = default)
    {
        var ids = uids.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0) return [];
        var sql = $"""
            SELECT {PurchaseDraftHeadColumns}
            FROM dbo.PurchaseOrderHead h
            WHERE h.Uid IN @Ids AND h.IsDeleted=0
            ORDER BY h.DemandSourceType,h.DemandSourceUid,h.SupplierId,h.Uid;
            """;
        var rows = await Session.Connection.QueryAsync<NetDemandPurchaseDraftHead>(new CommandDefinition(
            sql, new { Ids = ids }, Session.Transaction, cancellationToken: cancellationToken));
        return [.. rows];
    }

    public async Task<int> InsertPurchaseDraftAsync(NetDemandPurchaseDraftHead head)
    {
        ArgumentNullException.ThrowIfNull(head);
        if (head.Uid == Guid.Empty) throw new ArgumentException("采购草稿必须由 Application 预生成 Uid。", nameof(head));
        context.AuditStamp.StampForInsert(head);
        head.IsDeleted = false;
        if (head.Version <= 0) head.Version = 1;

        const string sql = """
            INSERT INTO dbo.PurchaseOrderHead
            (Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,
             OrderStatus,ConfirmationStatus,LifecycleStatus,OrderNo,BoNo,SalesOrderNo,CustomerPoNo,
             CustomerCode,Currency,SupplierCode,SupplierName,SupplierId,DemandSourceType,DemandSourceUid,
             DemandSourceNo,ExecutionRequestId,SoPerson,PoPerson,InletrHoseType,AeratorType,PalletType,
             BoxType,LabelType,IMType,CartonType,LogoType,ReviewedBy,PoFolder,ZheKouBeiZhu,Remark,
             IsLocked,LineCount,TotalAmountLocalCurrency,TotalQty,TotalShippedQty,TotalUnshippedQty,
             TotalReadyQty,OrderDate,ExpectedDate,ReplyDate,ZhiJieYouHuiJinE,ZheKou)
            VALUES
            (@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,
             @OrderStatus,@ConfirmationStatus,@LifecycleStatus,@OrderNo,NULL,NULL,NULL,
             NULL,@Currency,@SupplierCode,@SupplierName,@SupplierId,@DemandSourceType,@DemandSourceUid,
             @DemandSourceNo,@ExecutionRequestId,NULL,NULL,NULL,NULL,NULL,
             NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,@Remark,
             @IsLocked,@LineCount,@TotalAmountLocalCurrency,@TotalQty,@TotalShippedQty,@TotalUnshippedQty,
             @TotalReadyQty,@OrderDate,@ExpectedDate,NULL,NULL,NULL);
            """;
        return await Session.Connection.ExecuteAsync(sql, head, transaction: Session.Transaction);
    }

    public Task<int> GetMaxLineNumberForUpdateAsync(Guid purchaseOrderId)
    {
        if (purchaseOrderId == Guid.Empty) throw new ArgumentException("采购头 Uid 不能为空。", nameof(purchaseOrderId));
        return Session.Connection.ExecuteScalarAsync<int>("""
            SELECT COALESCE(MAX(LineNumber),0)
            FROM dbo.PurchaseOrderLine WITH (UPDLOCK,HOLDLOCK)
            WHERE PurchaseOrderId=@PurchaseOrderId AND IsDeleted=0;
            """, new { PurchaseOrderId = purchaseOrderId }, transaction: Session.Transaction);
    }

    public async Task<int> InsertPurchaseDraftLinesAsync(IEnumerable<NetDemandPurchaseDraftLine> lines)
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
             @DescriptionZh,NULL,NULL,@UnitPrice,@TaxPrice,@QTY,@UnitAmount,@TaxAmount,
             @PlannedDeliveryDate,@DeliveryTargetAllocationId,@DeliveryTargetSupplierId,
             @DeliverySupplierCodeSnapshot,@DeliverySupplierNameSnapshot,@SubNote,@SupplyType,@MaterialType,@DeliveryTo,
             @SourceType,@SourceNo,@SourceUid,@SourceLineId,@SourceLineNumber,@ReadyQty,@RecQty,NULL,
             NULL,NULL,NULL);
            """;
        return await Session.Connection.ExecuteAsync(sql, materialized, transaction: Session.Transaction);
    }

    public Task<int> RefreshDraftSummaryAfterAppendAsync(Guid uid, int expectedVersion)
        => RefreshDraftSummaryAsync(uid, expectedVersion, requireSafeFacts: true);

    public Task<NetDemandPurchaseDraftHead?> GetLifecycleDraftAsync(Guid uid, bool withUpdLock = false)
    {
        if (uid == Guid.Empty) throw new ArgumentException("采购头 Uid 不能为空。", nameof(uid));
        var lockHint = withUpdLock ? "WITH (UPDLOCK,HOLDLOCK)" : "WITH (NOLOCK)";
        var sql = $"""
            SELECT {PurchaseDraftHeadColumns}
            FROM dbo.PurchaseOrderHead h {lockHint}
            WHERE h.Uid=@Uid AND h.IsDeleted=0
              AND h.ExecutionRequestId IS NOT NULL
              AND h.DemandSourceType IS NOT NULL
              AND h.DemandSourceUid IS NOT NULL
              AND h.SupplierId IS NOT NULL
              AND h.LifecycleStatus IS NOT NULL;
            """;
        return Session.Connection.QuerySingleOrDefaultAsync<NetDemandPurchaseDraftHead>(
            sql, new { Uid = uid }, transaction: Session.Transaction);
    }

    public Task<bool> HasDownstreamFactsAsync(Guid uid, bool withUpdLock = false)
    {
        if (uid == Guid.Empty) throw new ArgumentException("采购头 Uid 不能为空。", nameof(uid));
        var predicate = withUpdLock ? SafeAppendFactsPredicateWithLocks : SafeAppendFactsPredicate;
        var headLock = withUpdLock ? "WITH (UPDLOCK,HOLDLOCK)" : "WITH (NOLOCK)";
        var sql = $"""
            SELECT CAST(CASE WHEN {predicate} THEN 0 ELSE 1 END AS bit)
            FROM dbo.PurchaseOrderHead h {headLock}
            WHERE h.Uid=@Uid AND h.IsDeleted=0;
            """;
        return Session.Connection.ExecuteScalarAsync<bool>(sql, new { Uid = uid }, transaction: Session.Transaction);
    }

    public async Task<IReadOnlyList<NetDemandPurchaseDraftLine>> GetDraftLinesForUpdateAsync(Guid purchaseOrderId)
    {
        if (purchaseOrderId == Guid.Empty) throw new ArgumentException("采购头 Uid 不能为空。", nameof(purchaseOrderId));
        var rows = await Session.Connection.QueryAsync<NetDemandPurchaseDraftLine>("""
            SELECT Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,
                   PurchaseOrderId,LineNumber,QTY,UnitPrice,TaxPrice
            FROM dbo.PurchaseOrderLine WITH (UPDLOCK,HOLDLOCK)
            WHERE PurchaseOrderId=@PurchaseOrderId AND IsDeleted=0
            ORDER BY LineNumber,Uid;
            """, new { PurchaseOrderId = purchaseOrderId }, transaction: Session.Transaction);
        return [.. rows];
    }

    public Task<int> VoidDraftAsync(Guid uid, int expectedVersion, string reason)
        => Session.Connection.ExecuteAsync("""
            UPDATE dbo.PurchaseOrderHead
            SET LifecycleStatus=@Voided,UpdatedDate=@Now,UpdatedBy=@UserName,
                Remark=CASE WHEN NULLIF(@Reason,N'') IS NULL THEN Remark
                            WHEN NULLIF(Remark,N'') IS NULL THEN @Reason
                            ELSE CONCAT(Remark,N'；',@Reason) END,
                Version=Version+1
            WHERE Uid=@Uid AND Version=@ExpectedVersion AND LifecycleStatus=@Active
              AND ConfirmationStatus=@Pending AND ExecutionRequestId IS NOT NULL
              AND DemandSourceType IS NOT NULL AND DemandSourceUid IS NOT NULL
              AND SupplierId IS NOT NULL AND IsDeleted=0;
            """, new
        {
            Uid = uid,
            ExpectedVersion = expectedVersion,
            Reason = reason,
            Active = PurchaseOrderLifecycleStatusEnum.Active,
            Voided = PurchaseOrderLifecycleStatusEnum.Voided,
            Pending = ConfirmationStatusEnum.待确认,
            Now = DateTime.Now,
            UserName = context.CurrentUser.UserName
        }, transaction: Session.Transaction);

    public async Task<int> UpdateDraftLineQuantitiesAsync(
        IReadOnlyDictionary<Guid, decimal> quantities,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(quantities);
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
            affected += await Session.Connection.ExecuteAsync(sql, new
            {
                Uid = item.Key,
                Qty = item.Value,
                Reason = reason,
                Now = DateTime.Now,
                UserName = context.CurrentUser.UserName
            }, transaction: Session.Transaction);
        }
        return affected;
    }

    public Task<int> RefreshDraftSummaryAfterReleaseAsync(Guid uid, int expectedVersion)
        => RefreshDraftSummaryAsync(uid, expectedVersion, requireSafeFacts: false);

    private Task<int> RefreshDraftSummaryAsync(Guid uid, int expectedVersion, bool requireSafeFacts)
    {
        if (uid == Guid.Empty) throw new ArgumentException("采购头 Uid 不能为空。", nameof(uid));
        if (expectedVersion <= 0) throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        var safeFacts = requireSafeFacts ? $"AND {SafeAppendFactsPredicateWithLocks}" : string.Empty;
        var sql = $"""
            UPDATE h
               SET LineCount=summary.LineCount,TotalQty=summary.TotalQty,
                   TotalAmountLocalCurrency=summary.TotalAmountLocalCurrency,
                   TotalUnshippedQty=summary.TotalQty-ISNULL(h.TotalShippedQty,0),
                   ExpectedDate=summary.EarliestPlannedDeliveryDate,
                   UpdatedDate=@Now,UpdatedBy=@UserName,Version=h.Version+1
            FROM dbo.PurchaseOrderHead h
            CROSS APPLY
            (
                SELECT COUNT_BIG(1) AS LineCount,COALESCE(SUM(l.QTY),0) AS TotalQty,
                       COALESCE(SUM(COALESCE(l.TaxPrice,l.UnitPrice,0)*l.QTY),0) AS TotalAmountLocalCurrency,
                       MIN(l.PlannedDeliveryDate) AS EarliestPlannedDeliveryDate
                FROM dbo.PurchaseOrderLine l WITH (UPDLOCK,HOLDLOCK)
                WHERE l.PurchaseOrderId=h.Uid AND l.IsDeleted=0 AND l.QTY>0
            ) summary
            WHERE h.Uid=@Uid AND h.Version=@ExpectedVersion AND h.LifecycleStatus=@Active
              AND h.ConfirmationStatus=@Pending AND h.IsDeleted=0 {safeFacts};
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

    private const string PurchaseDraftHeadColumns = """
        h.Uid,h.Version,h.IsDeleted,h.CreatedDate,h.CreatedBy,h.UpdatedDate,h.UpdatedBy,
        h.OrderStatus,h.ConfirmationStatus,h.LifecycleStatus,h.OrderNo,h.Currency,
        h.SupplierCode,h.SupplierName,h.SupplierId,h.DemandSourceType,h.DemandSourceUid,
        h.DemandSourceNo,h.ExecutionRequestId,h.Remark,h.IsLocked,h.LineCount,
        h.TotalAmountLocalCurrency,h.TotalQty,h.TotalShippedQty,h.TotalUnshippedQty,
        h.TotalReadyQty,h.OrderDate,h.ExpectedDate,h.ConfirmedDate,h.ConfirmedBy
        """;

    private const string SafeAppendFactsPredicate = """
        NOT EXISTS
        (
            SELECT 1 FROM dbo.PurchaseOrderLine lineFact
            WHERE lineFact.PurchaseOrderId=h.Uid AND ISNULL(lineFact.IsDeleted,0)=0
              AND (ISNULL(lineFact.RecQty,0)>0 OR lineFact.LastRecDate IS NOT NULL)
        )
        AND NOT EXISTS
        (
            SELECT 1 FROM dbo.PurchaseReceiveLine receiveFact
            WHERE ISNULL(receiveFact.IsDeleted,0)=0
              AND (receiveFact.PoLineId IN
                   (SELECT lineFact.Uid FROM dbo.PurchaseOrderLine lineFact
                    WHERE lineFact.PurchaseOrderId=h.Uid AND ISNULL(lineFact.IsDeleted,0)=0)
                   OR (receiveFact.PoLineId IS NULL AND receiveFact.PoNo=h.OrderNo))
        )
        AND NOT EXISTS
        (
            SELECT 1 FROM dbo.InventoryTransaction inventoryFact
            WHERE ISNULL(inventoryFact.IsDeleted,0)=0
              AND (inventoryFact.BizLineId IN
                   (SELECT lineFact.Uid FROM dbo.PurchaseOrderLine lineFact
                    WHERE lineFact.PurchaseOrderId=h.Uid AND ISNULL(lineFact.IsDeleted,0)=0)
                   OR inventoryFact.RefNo=h.OrderNo)
        )
        """;

    private const string SafeAppendFactsPredicateWithLocks = """
        NOT EXISTS
        (
            SELECT 1 FROM dbo.PurchaseOrderLine lineFact WITH (UPDLOCK,HOLDLOCK)
            WHERE lineFact.PurchaseOrderId=h.Uid AND ISNULL(lineFact.IsDeleted,0)=0
              AND (ISNULL(lineFact.RecQty,0)>0 OR lineFact.LastRecDate IS NOT NULL)
        )
        AND NOT EXISTS
        (
            SELECT 1 FROM dbo.PurchaseReceiveLine receiveFact WITH (UPDLOCK,HOLDLOCK)
            WHERE ISNULL(receiveFact.IsDeleted,0)=0
              AND (receiveFact.PoLineId IN
                   (SELECT lineFact.Uid FROM dbo.PurchaseOrderLine lineFact WITH (UPDLOCK,HOLDLOCK)
                    WHERE lineFact.PurchaseOrderId=h.Uid AND ISNULL(lineFact.IsDeleted,0)=0)
                   OR (receiveFact.PoLineId IS NULL AND receiveFact.PoNo=h.OrderNo))
        )
        AND NOT EXISTS
        (
            SELECT 1 FROM dbo.InventoryTransaction inventoryFact WITH (UPDLOCK,HOLDLOCK)
            WHERE ISNULL(inventoryFact.IsDeleted,0)=0
              AND (inventoryFact.BizLineId IN
                   (SELECT lineFact.Uid FROM dbo.PurchaseOrderLine lineFact WITH (UPDLOCK,HOLDLOCK)
                    WHERE lineFact.PurchaseOrderId=h.Uid AND ISNULL(lineFact.IsDeleted,0)=0)
                   OR inventoryFact.RefNo=h.OrderNo)
        )
        """;
}
