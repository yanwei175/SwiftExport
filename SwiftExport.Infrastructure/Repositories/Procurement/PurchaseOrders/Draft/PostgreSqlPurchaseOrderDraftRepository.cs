using System.Data;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Draft;

/// <summary>完整实现需求转采购草稿的PostgreSQL八方法接口；借用当前会话，保留固定写列及生命周期门禁，不接管来源事务。</summary>
public sealed class PostgreSqlPurchaseOrderDraftRepository(RepositoryBaseContext context) : IPurchaseOrderDraftRepository
{
    private static readonly EntityMetadata Head = PostgreSqlEntityMetadata.GetMetadata(typeof(PurchaseOrderHead));
    private static readonly EntityMetadata Line = PostgreSqlEntityMetadata.GetMetadata(typeof(PurchaseOrderLine));
    private static readonly EntityMetadata Receive = PostgreSqlEntityMetadata.GetMetadata(typeof(InboundDocumentLine));
    private static readonly EntityMetadata Ledger = PostgreSqlEntityMetadata.GetMetadata(typeof(InventoryLedgerEntry));
    // 原头投影只含44列；OriginKind、确认批次和包装要求不是本读取合同的一部分。
    private static readonly string[] HeadFields = [
        "Uid","Version","IsDeleted","CreatedDate","CreatedBy","UpdatedDate","UpdatedBy",
        "ConfirmationStatus","LifecycleStatus","OrderNo","BoNo","CustomerPoNo","CustomerId","CustomerCode","CustomerNameSnapshot","Currency","DiscountRate","DiscountAmount",
        "SupplierId","SupplierCode","SupplierName","DemandSourceType","DemandSourceUid","DemandSourceNo",
        "ExecutionRequestId","SoPerson","PoPerson","InletrHoseType","AeratorType","PalletType","BoxType",
        "LabelType","IMType","CartonType","LogoType","ReviewedBy","PoFolder","ZheKouBeiZhu","Remark",
        "OrderDate","ExpectedDate","ReplyDate","ConfirmedDate","ConfirmedBy"];

    /// <summary>普通读取指定来源和供应商的活动待确认安全追加候选；金额来自未删行快照，历史收货/流水阻断资格。</summary>
    public async Task<IReadOnlyList<SafePurchaseDraftCandidateDto>> GetSafeAppendCandidatesAsync(
        string sourceType, Guid sourceUid, IEnumerable<Guid> supplierIds, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceType)) throw new ArgumentException("来源类型不能为空。", nameof(sourceType));
        if (sourceUid == Guid.Empty) throw new ArgumentException("来源 Uid 不能为空。", nameof(sourceUid));
        var ids = supplierIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0) return [];
        var values = new PostgreSqlParameters();
        values.AddIds("SupplierIds", ids);
        values.Add("ActiveLifecycle", (int)PurchaseOrderLifecycleStatusEnum.Active);
        values.Add("PendingConfirmation", (int)ConfirmationStatusEnum.待确认);
        var sql = $"""
            SELECT {C(Head,"h","Uid")} AS "PurchaseOrderId",{C(Head,"h","Version")} AS "Version",
                   {C(Head,"h","OrderNo")} AS "OrderNo",{C(Head,"h","DemandSourceType")} AS "SourceType",
                   {C(Head,"h","DemandSourceUid")} AS "SourceUid",{C(Head,"h","SupplierId")} AS "SupplierId",
                   COALESCE(amounts.untaxed,0) AS "UntaxedTotalAmount",
                   COALESCE(amounts.tax_included,0) AS "TaxIncludedTotalAmount"
            FROM {Table(Head)} h
            LEFT JOIN LATERAL
            (
                SELECT SUM(COALESCE({C(Line,"l","UnitAmount")},0)) AS untaxed,
                       SUM(COALESCE({C(Line,"l","TaxAmount")},0)) AS tax_included
                FROM {Table(Line)} l
                WHERE {C(Line,"l","PurchaseOrderId")}={C(Head,"h","Uid")} AND {C(Line,"l","IsDeleted")}=FALSE
            ) amounts ON TRUE
            WHERE {Dictionary(C(Head,"h","DemandSourceType"))}=rtrim(@SourceType,' ') COLLATE public.erp_dictionary_ci
              AND {C(Head,"h","DemandSourceUid")}=@SourceUid
              AND {C(Head,"h","SupplierId")}=ANY(@SupplierIds)
              AND {C(Head,"h","LifecycleStatus")}=@ActiveLifecycle
              AND {C(Head,"h","ConfirmationStatus")}=@PendingConfirmation
              AND {C(Head,"h","IsDeleted")}=FALSE
              AND {SafeFacts()}
            ORDER BY {Dictionary(C(Head,"h","OrderNo"))} ASC NULLS FIRST,{UuidOrder(C(Head,"h","Uid"))} ASC NULLS FIRST;
            """;
        var rows = await context.Session.Connection.QueryAsync<SafePurchaseDraftCandidateDto>(new CommandDefinition(
            sql, new SourceParameters(values, sourceType.Trim(), sourceUid), context.Session.Transaction, cancellationToken: cancellationToken));
        return [.. rows];
    }

    /// <summary>普通批量读取未删采购头的原44列；独立及各种状态保留，缺失省略，空集合不执行SQL。</summary>
    public async Task<IReadOnlyList<PurchaseOrderHead>> GetPurchaseDraftsByIdsAsync(
        IEnumerable<Guid> uids, CancellationToken cancellationToken = default)
    {
        var ids = uids.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0) return [];
        var projection = string.Join(',', HeadFields.Select(x => C(Head, "h", x) + " AS " + PostgreSqlNames.Quote(x)));
        var sql = $"""
            SELECT {projection} FROM {Table(Head)} h
            WHERE {C(Head,"h","Uid")}=ANY(@Uids) AND {C(Head,"h","IsDeleted")}=FALSE
            ORDER BY {Dictionary(C(Head,"h","DemandSourceType"))} ASC NULLS FIRST,
                     {UuidOrder(C(Head,"h","DemandSourceUid"))} ASC NULLS FIRST,
                     {UuidOrder(C(Head,"h","SupplierId"))} ASC NULLS FIRST,
                     {UuidOrder(C(Head,"h","Uid"))} ASC NULLS FIRST;
            """;
        var rows = await context.Session.Connection.QueryAsync<PurchaseOrderHead>(new CommandDefinition(
            sql, PostgreSqlParameters.Ids(ids), context.Session.Transaction, cancellationToken: cancellationToken));
        return [.. rows];
    }

    /// <summary>按原执行请求、来源和供应商读取并锁定实际未删采购头；保留入口校验和Trim，缺失范围不虚构保护。</summary>
    public Task<PurchaseOrderHead?> GetByExecutionScopeForUpdateAsync(
        Guid executionRequestId, string sourceType, Guid sourceUid, Guid supplierId)
    {
        if (executionRequestId == Guid.Empty) throw new ArgumentException("执行请求 Uid 不能为空。", nameof(executionRequestId));
        if (string.IsNullOrWhiteSpace(sourceType)) throw new ArgumentException("来源类型不能为空。", nameof(sourceType));
        if (sourceUid == Guid.Empty) throw new ArgumentException("来源 Uid 不能为空。", nameof(sourceUid));
        if (supplierId == Guid.Empty) throw new ArgumentException("供应商 Uid 不能为空。", nameof(supplierId));
        var values = new PostgreSqlParameters();
        values.Add("ExecutionRequestId", executionRequestId);
        values.Add("SupplierId", supplierId);
        return context.Session.Connection.QuerySingleOrDefaultAsync<PurchaseOrderHead>($"""
            SELECT {LockedHeadProjection()} FROM {Table(Head)} h
            WHERE {C(Head,"h","ExecutionRequestId")}=@ExecutionRequestId
                AND {Dictionary(C(Head,"h","DemandSourceType"))}=rtrim(@SourceType,' ') COLLATE public.erp_dictionary_ci
                AND {C(Head,"h","DemandSourceUid")}=@SourceUid AND {C(Head,"h","SupplierId")}=@SupplierId
                AND {C(Head,"h","IsDeleted")}=FALSE FOR UPDATE OF h;
            """, new SourceParameters(values, sourceType.Trim(), sourceUid), context.Session.Transaction);
    }

    /// <summary>按原版本、活动待确认及安全事实资格锁定实际头；保留非空执行身份要求，不补OriginKind或其他业务资格。</summary>
    public Task<PurchaseOrderHead?> GetSafeDraftForUpdateAsync(Guid uid, int expectedVersion)
    {
        ValidateHeadKey(uid, expectedVersion);
        return context.Session.Connection.QuerySingleOrDefaultAsync<PurchaseOrderHead>($"""
            SELECT {LockedHeadProjection()} FROM {Table(Head)} h
            WHERE {C(Head,"h","Uid")}=@Uid AND {C(Head,"h","Version")}=@ExpectedVersion
                AND {C(Head,"h","LifecycleStatus")}=@ActiveLifecycle AND {C(Head,"h","ConfirmationStatus")}=@PendingConfirmation
                AND {Dictionary(C(Head,"h","DemandSourceType"))}<>''
                AND {C(Head,"h","DemandSourceUid")}<>@EmptyUid AND {C(Head,"h","SupplierId")}<>@EmptyUid
                AND {C(Head,"h","ExecutionRequestId")}<>@EmptyUid AND {C(Head,"h","IsDeleted")}=FALSE
                AND {SafeFactsWithLocks()} FOR UPDATE OF h;
            """, new { Uid = uid, ExpectedVersion = expectedVersion, EmptyUid = Guid.Empty,
                ActiveLifecycle = (int)PurchaseOrderLifecycleStatusEnum.Active,
                PendingConfirmation = (int)ConfirmationStatusEnum.待确认 }, context.Session.Transaction);
    }

    /// <summary>按原45固定头字段新增；保留Application生成的Uid，打戳后清软删，仅非正版本调整为1，返回影响行数。</summary>
    public async Task<int> InsertPurchaseDraftAsync(PurchaseOrderHead head)
    {
        ArgumentNullException.ThrowIfNull(head);
        if (head.Uid == Guid.Empty) throw new ArgumentException("采购草稿必须由 Application 预生成 Uid。", nameof(head));
        context.AuditStamp.StampForInsert(head);
        head.IsDeleted = false;
        if (head.Version <= 0) head.Version = 1;
        return await context.Session.Connection.ExecuteAsync(
            $"INSERT INTO {Table(Head)} ({string.Join(',', InsertHeadFields.Select(x => WriteColumn(Head, x)))}) VALUES ({string.Join(',', InsertHeadFields.Select(x => "@" + x))});",
            PostgreSqlParameters.Entity(head, Head), context.Session.Transaction);
    }

    /// <summary>锁现有未删明细后聚合最大行号；父头未删才计入，空集返回0，不宣称明细空范围已加锁。</summary>
    public Task<int> GetMaxLineNumberForUpdateAsync(Guid purchaseOrderId)
    {
        if (purchaseOrderId == Guid.Empty) throw new ArgumentException("采购头 Uid 不能为空。", nameof(purchaseOrderId));
        return context.Session.Connection.ExecuteScalarAsync<int>($"""
            WITH locked_lines AS MATERIALIZED (
                SELECT {C(Line,"l","LineNumber")} AS line_number FROM {Table(Line)} l
                WHERE {C(Line,"l","PurchaseOrderId")}=@PurchaseOrderId AND {C(Line,"l","IsDeleted")}=FALSE
                    AND EXISTS (SELECT 1 FROM {Table(Head)} owner WHERE {C(Head,"owner","Uid")}=@PurchaseOrderId
                        AND {C(Head,"owner","IsDeleted")}=FALSE)
                FOR UPDATE OF l
            ) SELECT COALESCE(MAX(line_number),0) FROM locked_lines;
            """, new { PurchaseOrderId = purchaseOrderId }, context.Session.Transaction);
    }

    /// <summary>先物化和校验全部行身份，再统一打戳；按输入顺序锁现有父头并插入原固定列，缺父头立即失败，由外层事务回滚。</summary>
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
        // 单语句将父头锁与INSERT关联，避免C#先查头后插入之间引入新窗口；空owner不写行，随后抛原中文失败。
        var sql = $"""
            WITH owner AS MATERIALIZED (
                SELECT {C(Head,"h","Uid")} FROM {Table(Head)} h
                WHERE {C(Head,"h","Uid")}=@PurchaseOrderId AND {C(Head,"h","IsDeleted")}=FALSE FOR UPDATE OF h
            )
            INSERT INTO {Table(Line)} ({string.Join(',', InsertLineFields.Select(x => WriteColumn(Line, x)))})
            SELECT {string.Join(',', InsertLineFields.Select(x => "@" + x))} WHERE EXISTS (SELECT 1 FROM owner);
            """;
        var count = 0;
        foreach (var line in materialized)
        {
            var affected = await context.Session.Connection.ExecuteAsync(sql, PostgreSqlParameters.Entity(line, Line), context.Session.Transaction);
            if (affected == 0) throw new InvalidOperationException("采购单不存在或已删除，不能追加采购行。");
            count += affected;
        }
        return count;
    }

    /// <summary>按原乐观版本和安全门禁推进头版本，重算未删正数量行最早交期；直接写后台时间，不回写调用方头实体。</summary>
    public Task<int> AdvanceDraftVersionAfterAppendAsync(Guid uid, int expectedVersion)
    {
        ValidateHeadKey(uid, expectedVersion);
        var parameters = new PostgreSqlParameters();
        parameters.Add("Uid", uid);
        parameters.Add("ExpectedVersion", expectedVersion);
        parameters.Add("Active", (int)PurchaseOrderLifecycleStatusEnum.Active);
        parameters.Add("Pending", (int)ConfirmationStatusEnum.待确认);
        parameters.AddTimestamp("Now", DateTime.Now);
        parameters.Add("UserName", context.CurrentUser.UserName);
        // 锁定实际合格头及实际正数量行后才MIN，避免对聚合结果非法FOR UPDATE；缺行/新收货范围仍由联合协议验收。
        return context.Session.Connection.ExecuteAsync($"""
            WITH candidate AS MATERIALIZED (
                SELECT {C(Head,"h","Uid")} AS uid FROM {Table(Head)} h
                WHERE {C(Head,"h","Uid")}=@Uid AND {C(Head,"h","Version")}=@ExpectedVersion
                    AND {C(Head,"h","LifecycleStatus")}=@Active AND {C(Head,"h","ConfirmationStatus")}=@Pending
                    AND {C(Head,"h","IsDeleted")}=FALSE AND {SafeFactsWithLocks()} FOR UPDATE OF h
            ), locked_dates AS MATERIALIZED (
                SELECT {C(Line,"l","PlannedDeliveryDate")} AS planned_date FROM {Table(Line)} l
                JOIN candidate owner ON owner.uid={C(Line,"l","PurchaseOrderId")}
                WHERE {C(Line,"l","IsDeleted")}=FALSE AND {C(Line,"l","Quantity")}>0 FOR UPDATE OF l
            ), facts AS (SELECT MIN(planned_date) AS earliest_date FROM locked_dates)
            UPDATE {Table(Head)} h SET {WriteColumn(Head,"ExpectedDate")}=facts.earliest_date,
                {WriteColumn(Head,"UpdatedDate")}=@Now,{WriteColumn(Head,"UpdatedBy")}=@UserName,
                {WriteColumn(Head,"Version")}={C(Head,"h","Version")}+1
            FROM candidate owner CROSS JOIN facts
            WHERE {C(Head,"h","Uid")}=owner.uid AND {C(Head,"h","Version")}=@ExpectedVersion;
            """, parameters, context.Session.Transaction);
    }

    // 新增只用原固定写列；读取44列不扩展到OriginKind，行Quantity按共享映射对应原SQL Server的QTY。
    private static readonly string[] InsertHeadFields = ["Uid","Version","IsDeleted","CreatedDate","CreatedBy","UpdatedDate","UpdatedBy","OriginKind","ConfirmationStatus","LifecycleStatus","OrderNo","BoNo","CustomerPoNo","CustomerId","CustomerCode","CustomerNameSnapshot","Currency","DiscountRate","DiscountAmount","SupplierId","SupplierCode","SupplierName","DemandSourceType","DemandSourceUid","DemandSourceNo","ExecutionRequestId","SoPerson","PoPerson","InletrHoseType","AeratorType","PalletType","BoxType","LabelType","IMType","CartonType","LogoType","ReviewedBy","PoFolder","ZheKouBeiZhu","Remark","OrderDate","ExpectedDate","ReplyDate","ConfirmedDate","ConfirmedBy"];
    private static readonly string[] InsertLineFields = ["Uid","Version","IsDeleted","CreatedDate","CreatedBy","UpdatedDate","UpdatedBy","PurchaseOrderId","LineNumber","OrderNo","ProductId","Sku","CustomerSku","LinkSku","SupplierSku","DescriptionZh","FinishZh","FeaturesZh","UnitSnapshot","Quantity","UnlinkedQty","UnitPrice","TaxPrice","UnitAmount","TaxAmount","PlannedDeliveryDate","DeliveryTargetSupplierId","DeliverySupplierCodeSnapshot","DeliverySupplierNameSnapshot","SubNote","SupplyType","MaterialType","SourceType","SourceNo","SourceUid","SourceLineId","SourceLineNumber"];

    /// <summary>沿原头身份和乐观版本校验顺序，在SQL前拒绝空身份或非正版本。</summary>
    private static void ValidateHeadKey(Guid uid, int expectedVersion)
    {
        if (uid == Guid.Empty) throw new ArgumentException("采购头 Uid 不能为空。", nameof(uid));
        if (expectedVersion <= 0) throw new ArgumentOutOfRangeException(nameof(expectedVersion));
    }

    /// <summary>锁定读取仍复用原44固定列与显式CLR别名，不改变已交付普通读取。</summary>
    private static string LockedHeadProjection() => string.Join(',', HeadFields.Select(x => C(Head, "h", x) + " AS " + PostgreSqlNames.Quote(x)));

    /// <summary>固定写列引用共享物理映射，参数名仍保持原CLR属性。</summary>
    private static string WriteColumn(EntityMetadata map, string property) => PostgreSqlNames.Quote(map.GetProperty(property).ColumnName);

    /// <summary>保留原入库/流水门禁及软删采购行范围，锁实际匹配行和关联采购行；不存在的下游范围不虚构保护。</summary>
    private static string SafeFactsWithLocks() => $"""
        NOT EXISTS (
            SELECT 1 FROM {Table(Receive)} r
            WHERE COALESCE({C(Receive,"r","IsDeleted")},FALSE)=FALSE AND {C(Receive,"r","SourceLineId")} IN
                (SELECT {C(Line,"p","Uid")} FROM {Table(Line)} p WHERE {C(Line,"p","PurchaseOrderId")}={C(Head,"h","Uid")} FOR UPDATE OF p)
            FOR UPDATE OF r
        ) AND NOT EXISTS (
            SELECT 1 FROM {Table(Ledger)} e JOIN {Table(Receive)} history ON {C(Receive,"history","Uid")}={C(Ledger,"e","SourceLineId")}
            WHERE {C(Receive,"history","SourceLineId")} IN
                (SELECT {C(Line,"p","Uid")} FROM {Table(Line)} p WHERE {C(Line,"p","PurchaseOrderId")}={C(Head,"h","Uid")} FOR UPDATE OF p)
            FOR UPDATE OF e,history
        )
        """;

    /// <summary>保留全部采购行身份与收货历史；软删采购行、软删中间入库及反向流水均不能隐藏库存事实。</summary>
    private static string SafeFacts() => $"""
        NOT EXISTS
        (
            SELECT 1 FROM {Table(Receive)} r
            WHERE COALESCE({C(Receive,"r","IsDeleted")},FALSE)=FALSE
              AND {C(Receive,"r","SourceLineId")} IN
                  (SELECT {C(Line,"p","Uid")} FROM {Table(Line)} p WHERE {C(Line,"p","PurchaseOrderId")}={C(Head,"h","Uid")})
        )
        AND NOT EXISTS
        (
            SELECT 1 FROM {Table(Ledger)} e
            JOIN {Table(Receive)} history ON {C(Receive,"history","Uid")}={C(Ledger,"e","SourceLineId")}
            WHERE {C(Receive,"history","SourceLineId")} IN
                  (SELECT {C(Line,"p","Uid")} FROM {Table(Line)} p WHERE {C(Line,"p","PurchaseOrderId")}={C(Head,"h","Uid")})
        )
        """;

    /// <summary>只引用现有元数据的固定表列，不接受外部标识符。</summary>
    private static string Table(EntityMetadata map) => PostgreSqlNames.Quote(map.SchemaName) + "." + PostgreSqlNames.Quote(map.TableName);
    private static string C(EntityMetadata map, string alias, string property) => alias + "." + PostgreSqlNames.Quote(map.GetProperty(property).ColumnName);
    /// <summary>沿当前已批准尾ASCII空格及ICU口径，广域来源等值仍由主控专项验收。</summary>
    private static string Dictionary(string column) => $"rtrim({column},' ') COLLATE public.erp_dictionary_ci";
    /// <summary>复现SqlGuid比较权重；nullable来源UUID先返回SQL NULL，避免全NULL数组排到真实UUID之后。</summary>
    private static string UuidOrder(string column) => $"CASE WHEN {column} IS NULL THEN NULL ELSE ARRAY[" +
        string.Join(',', new[] { 10,11,12,13,14,15,8,9,7,6,5,4,3,2,1,0 }.Select(x => $"get_byte(uuid_send({column}),{x})")) + "] END";

    /// <summary>为来源文本和UUID补充明确类型，其余标量和UUID数组复用已有参数包及克隆规则。</summary>
    private sealed class SourceParameters(PostgreSqlParameters values, string sourceType, Guid sourceUid) : SqlMapper.IDynamicParameters
    {
        /// <summary>绑定原Trim后的文本；不修改存储值，不接管连接、事务或取消。</summary>
        public void AddParameters(IDbCommand command, SqlMapper.Identity identity)
        {
            values.AddParameters(command, identity);
            command.Parameters.Add(new NpgsqlParameter("SourceType", NpgsqlDbType.Text) { Value = sourceType });
            command.Parameters.Add(new NpgsqlParameter("SourceUid", NpgsqlDbType.Uuid) { Value = sourceUid });
        }
    }
}
