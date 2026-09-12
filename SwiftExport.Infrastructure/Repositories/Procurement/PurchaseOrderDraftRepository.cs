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
             ConfirmationStatus,LifecycleStatus,OrderNo,BoNo,CustomerPoNo,CustomerCode,Currency,DiscountRate,DiscountAmount,
             SupplierId,SupplierCode,SupplierName,DemandSourceType,DemandSourceUid,DemandSourceNo,
             ExecutionRequestId,SoPerson,PoPerson,InletrHoseType,AeratorType,PalletType,BoxType,
             LabelType,IMType,CartonType,LogoType,ReviewedBy,PoFolder,ZheKouBeiZhu,Remark,
             OrderDate,ExpectedDate,ReplyDate,ConfirmedDate,ConfirmedBy)
            VALUES
            (@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,
             @ConfirmationStatus,@LifecycleStatus,@OrderNo,@BoNo,@CustomerPoNo,@CustomerCode,@Currency,@DiscountRate,@DiscountAmount,
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
             DescriptionZh,FinishZh,FeaturesZh,UnitSnapshot,QTY,UnitPrice,TaxPrice,UnitAmount,TaxAmount,
             PlannedDeliveryDate,DeliveryTargetSupplierId,
             DeliverySupplierCodeSnapshot,DeliverySupplierNameSnapshot,SubNote,SupplyType,MaterialType,
             SourceType,SourceNo,SourceUid,SourceLineId,SourceLineNumber)
            VALUES
            (@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,
             @PurchaseOrderId,@LineNumber,@OrderNo,@ProductId,@Sku,@CustomerSku,@LinkSku,@SupplierSku,
             @DescriptionZh,@FinishZh,@FeaturesZh,@UnitSnapshot,@Quantity,@UnitPrice,@TaxPrice,@UnitAmount,@TaxAmount,
             @PlannedDeliveryDate,@DeliveryTargetSupplierId,
             @DeliverySupplierCodeSnapshot,@DeliverySupplierNameSnapshot,@SubNote,@SupplyType,@MaterialType,
             @SourceType,@SourceNo,@SourceUid,@SourceLineId,@SourceLineNumber);
            """;
        return await Session.Connection.ExecuteAsync(sql, materialized, transaction: Session.Transaction);
    }

    public Task<int> AdvanceDraftVersionAfterAppendAsync(Guid uid, int expectedVersion)
        => AdvanceDraftVersionAsync(uid, expectedVersion, requireSafeFacts: true);

    /// <summary>读取或锁定旧链来源完整的草稿，供取消及减量。</summary>
    public Task<PurchaseOrderHead?> GetLifecycleDraftAsync(Guid uid, bool withUpdLock = false)
    {
        if (uid == Guid.Empty) throw new ArgumentException("采购头 Uid 不能为空。", nameof(uid));
        var lockHint = withUpdLock ? "WITH (UPDLOCK,HOLDLOCK)" : "WITH (NOLOCK)";
        var sql = $"""
            SELECT {HeadColumns}
            FROM dbo.PurchaseOrderHead h {lockHint}
            WHERE h.Uid=@Uid AND h.IsDeleted=0
              AND h.ExecutionRequestId<>@EmptyUid
              AND h.DemandSourceType<>N''
              AND h.DemandSourceUid<>@EmptyUid
              AND h.SupplierId<>@EmptyUid;
            """;
        return Session.Connection.QuerySingleOrDefaultAsync<PurchaseOrderHead>(
            sql, new { Uid = uid, EmptyUid = Guid.Empty }, transaction: Session.Transaction);
    }

    /// <summary>检查旧链采购的收货及库存事实，不接管其他执行链。</summary>
    public Task<bool> HasDownstreamFactsAsync(Guid uid, bool withUpdLock = false)
    {
        if (uid == Guid.Empty) throw new ArgumentException("采购头 Uid 不能为空。", nameof(uid));
        var predicate = withUpdLock ? SafeFactsPredicateWithLocks : SafeFactsPredicate;
        var headLock = withUpdLock ? "WITH (UPDLOCK,HOLDLOCK)" : "WITH (NOLOCK)";
        var sql = $"SELECT CAST(CASE WHEN {predicate} THEN 0 ELSE 1 END AS bit) FROM dbo.PurchaseOrderHead h {headLock} WHERE h.Uid=@Uid AND h.IsDeleted=0;";
        return Session.Connection.ExecuteScalarAsync<bool>(sql, new { Uid = uid }, transaction: Session.Transaction);
    }

    /// <summary>锁定旧链采购行，保留减量执行所需数量与价格。</summary>
    public async Task<IReadOnlyList<PurchaseOrderLine>> GetDraftLinesForUpdateAsync(Guid purchaseOrderId)
    {
        if (purchaseOrderId == Guid.Empty) throw new ArgumentException("采购头 Uid 不能为空。", nameof(purchaseOrderId));
        var rows = await Session.Connection.QueryAsync<PurchaseOrderLine>("""
            SELECT Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,
                   PurchaseOrderId,LineNumber,QTY AS Quantity,UnitPrice,TaxPrice
            FROM dbo.PurchaseOrderLine WITH (UPDLOCK,HOLDLOCK)
            WHERE PurchaseOrderId=@PurchaseOrderId AND IsDeleted=0
              AND EXISTS(SELECT 1 FROM dbo.PurchaseOrderHead owner WHERE owner.Uid=@PurchaseOrderId AND owner.IsDeleted=0)
            ORDER BY LineNumber,Uid;
            """, new { PurchaseOrderId = purchaseOrderId }, transaction: Session.Transaction);
        return [.. rows];
    }

    /// <summary>在版本与旧链所有权一致时作废安全采购草稿。</summary>
    public Task<int> VoidDraftAsync(Guid uid, int expectedVersion, string reason)
        => Session.Connection.ExecuteAsync("""
            UPDATE dbo.PurchaseOrderHead
            SET LifecycleStatus=@Voided,UpdatedDate=@Now,UpdatedBy=@UserName,
                Remark=CASE WHEN NULLIF(@Reason,N'') IS NULL THEN Remark
                            WHEN NULLIF(Remark,N'') IS NULL THEN @Reason
                            ELSE CONCAT(Remark,N'；',@Reason) END,
                Version=Version+1
            WHERE Uid=@Uid AND Version=@ExpectedVersion AND LifecycleStatus=@Active
              AND ConfirmationStatus=@Pending AND ExecutionRequestId<>@EmptyUid
              AND DemandSourceType<>N'' AND DemandSourceUid<>@EmptyUid
              AND SupplierId<>@EmptyUid AND IsDeleted=0;
            """, new
        {
            Uid = uid,
            ExpectedVersion = expectedVersion,
            Reason = reason,
            EmptyUid = Guid.Empty,
            Active = PurchaseOrderLifecycleStatusEnum.Active,
            Voided = PurchaseOrderLifecycleStatusEnum.Voided,
            Pending = ConfirmationStatusEnum.待确认,
            Now = DateTime.Now,
            UserName = context.CurrentUser.UserName
        }, transaction: Session.Transaction);

    /// <summary>更新旧链采购行数量和金额，禁止跨执行链减量。</summary>
    public async Task<int> UpdateDraftLineQuantitiesAsync(
        IReadOnlyDictionary<Guid, decimal> quantities,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(quantities);
        var affected = 0;
        const string sql = """
            UPDATE dbo.PurchaseOrderLine
            SET QTY=@Quantity,UnitAmount=CASE WHEN UnitPrice IS NULL THEN NULL ELSE UnitPrice*@Quantity END,
                TaxAmount=CASE WHEN TaxPrice IS NULL THEN NULL ELSE TaxPrice*@Quantity END,
                SubNote=CASE WHEN NULLIF(@Reason,N'') IS NULL THEN SubNote
                             WHEN NULLIF(SubNote,N'') IS NULL THEN @Reason
                             ELSE CONCAT(SubNote,N'；',@Reason) END,
                UpdatedDate=@Now,UpdatedBy=@UserName,Version=Version+1
            WHERE Uid=@Uid AND IsDeleted=0
              AND EXISTS(SELECT 1 FROM dbo.PurchaseOrderHead owner WHERE owner.Uid=dbo.PurchaseOrderLine.PurchaseOrderId AND owner.IsDeleted=0);
            """;
        foreach (var item in quantities)
        {
            affected += await Session.Connection.ExecuteAsync(sql, new
            {
                Uid = item.Key,
                Quantity = item.Value,
                Reason = reason,
                Now = DateTime.Now,
                UserName = context.CurrentUser.UserName
            }, transaction: Session.Transaction);
        }
        return affected;
    }

    public Task<int> AdvanceDraftVersionAfterReleaseAsync(Guid uid, int expectedVersion)
        => AdvanceDraftVersionAsync(uid, expectedVersion, requireSafeFacts: false);

    /// <summary>仅推进旧链采购头版本并重算交期，按调用用途保留下游事实门禁。</summary>
    private Task<int> AdvanceDraftVersionAsync(Guid uid, int expectedVersion, bool requireSafeFacts)
    {
        ValidateHeadKey(uid, expectedVersion);
        var safeFacts = requireSafeFacts ? $"AND {SafeFactsPredicateWithLocks}" : string.Empty;
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

    private static void ValidateHeadKey(Guid uid, int expectedVersion)
    {
        if (uid == Guid.Empty) throw new ArgumentException("采购头 Uid 不能为空。", nameof(uid));
        if (expectedVersion <= 0) throw new ArgumentOutOfRangeException(nameof(expectedVersion));
    }

    private const string HeadColumns = """
        h.Uid,h.Version,h.IsDeleted,h.CreatedDate,h.CreatedBy,h.UpdatedDate,h.UpdatedBy,
        h.ConfirmationStatus,h.LifecycleStatus,h.OrderNo,h.BoNo,h.CustomerPoNo,h.CustomerCode,h.Currency,h.DiscountRate,h.DiscountAmount,
        h.SupplierId,h.SupplierCode,h.SupplierName,h.DemandSourceType,h.DemandSourceUid,h.DemandSourceNo,
        h.ExecutionRequestId,h.SoPerson,h.PoPerson,h.InletrHoseType,h.AeratorType,h.PalletType,h.BoxType,
        h.LabelType,h.IMType,h.CartonType,h.LogoType,h.ReviewedBy,h.PoFolder,h.ZheKouBeiZhu,h.Remark,
        h.OrderDate,h.ExpectedDate,h.ReplyDate,h.ConfirmedDate,h.ConfirmedBy
        """;

    private const string SafeFactsPredicate = """
        NOT EXISTS
        (
            SELECT 1 FROM dbo.InboundDocumentLine receiveFact
            WHERE ISNULL(receiveFact.IsDeleted,0)=0
              AND receiveFact.SourceType=10 AND receiveFact.SourceUid IN
                   (SELECT linkFact.Uid FROM dbo.DemandExecutionLink linkFact
                    WHERE linkFact.TargetHeadId=h.Uid AND linkFact.TargetType=N'PO' AND ISNULL(linkFact.IsDeleted,0)=0)
        )
        AND NOT EXISTS
        (
            SELECT 1 FROM dbo.InventoryLedgerEntry inventoryFact
            JOIN dbo.InboundDocumentLine receiveFact ON receiveFact.Uid=inventoryFact.SourceLineId
            WHERE receiveFact.SourceType=10 AND receiveFact.SourceUid IN
                  (SELECT linkFact.Uid FROM dbo.DemandExecutionLink linkFact
                   WHERE linkFact.TargetHeadId=h.Uid AND linkFact.TargetType=N'PO' AND ISNULL(linkFact.IsDeleted,0)=0)
        )
        """;

    private const string SafeFactsPredicateWithLocks = """
        NOT EXISTS
        (
            SELECT 1 FROM dbo.InboundDocumentLine receiveFact WITH (UPDLOCK,HOLDLOCK)
            WHERE ISNULL(receiveFact.IsDeleted,0)=0
              AND receiveFact.SourceType=10 AND receiveFact.SourceUid IN
                   (SELECT linkFact.Uid FROM dbo.DemandExecutionLink linkFact WITH (UPDLOCK,HOLDLOCK)
                    WHERE linkFact.TargetHeadId=h.Uid AND linkFact.TargetType=N'PO' AND ISNULL(linkFact.IsDeleted,0)=0)
        )
        AND NOT EXISTS
        (
            SELECT 1 FROM dbo.InventoryLedgerEntry inventoryFact WITH (UPDLOCK,HOLDLOCK)
            JOIN dbo.InboundDocumentLine receiveFact WITH (UPDLOCK,HOLDLOCK) ON receiveFact.Uid=inventoryFact.SourceLineId
            WHERE receiveFact.SourceType=10 AND receiveFact.SourceUid IN
                  (SELECT linkFact.Uid FROM dbo.DemandExecutionLink linkFact WITH (UPDLOCK,HOLDLOCK)
                   WHERE linkFact.TargetHeadId=h.Uid AND linkFact.TargetType=N'PO' AND ISNULL(linkFact.IsDeleted,0)=0)
        )
        """;
}
