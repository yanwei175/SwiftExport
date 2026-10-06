using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.DemandPlanning.Materials.Query;
using SwiftExport.Application.Services.DemandPlanning.Sources.Query;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand.Trace;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand.Trace;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Query;
using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Query;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>主控单事务回滚真实PG历史证明；不修改约束/触发器/旧行，不消耗或重置流水序列。</summary>
[TestClass]
[DoNotParallelize]
[TestCategory("PostgreSqlInboundHistoryIntegration")]
public sealed class PostgreSqlInboundHistoryMigrationIntegrationTests
{
    /// <summary>完整人工/自动原反向流水及SQL源大小写/尾空格比较保留可信历史，不自行计算数量。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CompleteReversal_Passes(bool automatic)
    {
        await InRollback(async f =>
        {
            var head = f.Head(); if (automatic) { head.CreationMode = InboundCreationModeEnum.Automatic; head.TriggerPackingListId = Guid.NewGuid(); }
            var line = f.Line(head); await f.Add(head); await f.Add(line);
            await f.Pair(head, line, "valid");
            await PostgreSqlInboundReceiptHistoryGuard.ValidateScopedAsync(f.Session, [line.SourceLineId]);
            Assert.AreSame(f.Transaction, f.Session.Transaction);
        });
    }

    /// <summary>正数已过账采购行不强求反向证明，保持源门禁的posting=1规则。</summary>
    [TestMethod]
    public async Task PostedPositiveLine_PassesWithoutReversalProof()
    {
        await InRollback(async f =>
        {
            var head = f.Head(); head.PostingStatus = InboundPostingStatusEnum.Posted;
            var line = f.Line(head); await f.Add(head); await f.Add(line);
            await PostgreSqlInboundReceiptHistoryGuard.ValidateScopedAsync(f.Session, [line.SourceLineId]);
        });
    }

    /// <summary>每一类明确不完整反向证据都阻止可信净收货，未知类别不能代替专用历史信号。</summary>
    [TestMethod]
    [DataRow("missingReverse")]
    [DataRow("wrongQty")]
    [DataRow("wrongProduct")]
    [DataRow("wrongWarehouse")]
    [DataRow("wrongUnit")]
    [DataRow("wrongCommandItem")]
    [DataRow("correction")]
    [DataRow("externalReplacement")]
    [DataRow("wrongCreation")]
    [DataRow("missingDate")]
    [DataRow("blankReason")]
    [DataRow("originalLine")]
    public async Task IncompleteEvidence_Blocks(string mode)
    {
        await InRollback(async f =>
        {
            var head = f.Head(); var line = f.Line(head);
            if (mode == "wrongCreation") head.TriggerPackingListId = Guid.NewGuid();
            if (mode == "missingDate") head.ReversedDate = null;
            if (mode == "blankReason") head.ReverseReason = "   ";
            if (mode == "originalLine") line.OriginalLineId = Guid.NewGuid();
            await f.Add(head); await f.Add(line); await f.Pair(head, line, mode);
            var failure = await Assert.ThrowsAsync<UnverifiedReceiptHistoryException>(() => PostgreSqlInboundReceiptHistoryGuard.ValidateScopedAsync(f.Session, [line.SourceLineId]));
            Assert.AreEqual(DatabaseFailureKind.UnverifiedReceiptHistory, f.Session.Errors.Classify(failure));
        });
    }

    /// <summary>软删范围行仍定位整头；无有效行的相关头不能被错误的scope软删过滤略过。</summary>
    [TestMethod]
    public async Task DeletedScopeLine_StillChecksWholeHead()
    {
        await InRollback(async f =>
        {
            var head = f.Head(); var line = f.Line(head); line.IsDeleted = true;
            await f.Add(head); await f.Add(line);
            await Assert.ThrowsAsync<UnverifiedReceiptHistoryException>(() => PostgreSqlInboundReceiptHistoryGuard.ValidateScopedAsync(f.Session, [line.SourceLineId]));
        });
    }

    /// <summary>已选采购行完整，但同一相关头的另一有效行没有流水时，整头证明仍失败。</summary>
    [TestMethod]
    public async Task RelatedOtherLine_IsNotOmitted()
    {
        await InRollback(async f =>
        {
            var head = f.Head(); var first = f.Line(head); var second = f.Line(head); second.LineNumber = 2;
            await f.Add(head); await f.Add(first); await f.Add(second); await f.Pair(head, first, "valid");
            await Assert.ThrowsAsync<UnverifiedReceiptHistoryException>(() => PostgreSqlInboundReceiptHistoryGuard.ValidateScopedAsync(f.Session, [first.SourceLineId]));
        });
    }

    /// <summary>无关坏入库头不阻断空/不相关scope，而全局门禁必须检测到；两种门禁不能互换。</summary>
    [TestMethod]
    public async Task UnrelatedBrokenHead_DoesNotExpandScope()
    {
        await InRollback(async f =>
        {
            var head = f.Head(); await f.Add(head);
            await PostgreSqlInboundReceiptHistoryGuard.ValidateScopedAsync(f.Session, []);
            await PostgreSqlInboundReceiptHistoryGuard.ValidateScopedAsync(f.Session, [Guid.NewGuid()]);
            await Assert.ThrowsAsync<UnverifiedReceiptHistoryException>(() => PostgreSqlInboundReceiptHistoryGuard.ValidateGlobalAsync(f.Session));
        });
    }

    /// <summary>未过账、非采购类型及删除头原本不属于门禁，不因为缺失有效行扩展检查范围。</summary>
    [TestMethod]
    [DataRow("unposted")]
    [DataRow("otherType")]
    [DataRow("deleted")]
    public async Task ExcludedHeads_AreIgnored(string mode)
    {
        await InRollback(async f =>
        {
            var head = f.Head(); var line = f.Line(head); line.IsDeleted = true;
            if (mode == "unposted") head.PostingStatus = InboundPostingStatusEnum.Unposted;
            if (mode == "otherType") head.DocumentType = InboundDocumentTypeEnum.ProductionReceipt;
            if (mode == "deleted") head.IsDeleted = true;
            await f.Add(head); await f.Add(line);
            await PostgreSqlInboundReceiptHistoryGuard.ValidateScopedAsync(f.Session, [line.SourceLineId]);
        });
    }

    /// <summary>两种原查询服务收到真实PG门禁信号仍返回既有“收货量未知”提示，不走普通失败分支。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExistingServices_PreserveUnknownReceiptMessage(bool material)
    {
        await InRollback(async f =>
        {
            var head = f.Head(); var line = f.Line(head); await f.Add(head); await f.Add(line);
            var context = new ServiceBaseContext(f.Session, NullLoggerFactory.Instance); var queries = new TraceSignal(f.Session, line.SourceLineId);
            var message = material
                ? (await new MaterialDemandQueryService(context, queries).GetTraceAsync(Guid.NewGuid())).Message
                : (await new GrossDemandQueryService(context, queries).GetTraceAsync(Guid.NewGuid())).Message;
            Assert.AreEqual("正式入库反向历史需核实，收货量未知。", message);
        });
    }

    /// <summary>预取消即使空范围也原样传播，不把取消当历史不完整。</summary>
    [TestMethod]
    public async Task Cancellation_IsNotHistoryFailure()
        => await InRollback(f => Assert.ThrowsAsync<OperationCanceledException>(() => PostgreSqlInboundReceiptHistoryGuard.ValidateScopedAsync(f.Session, [], new CancellationToken(true))));

    /// <summary>单事务随机夹具始终回滚，原追加流水不清理；实际整数序号从已存在max后局部取得，不消费数据库序列。</summary>
    private static async Task InRollback(Func<Fixture, Task> action)
    {
        using var session = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        using var transaction = session.Connection.BeginTransaction(); session.Transaction = transaction;
        var user = new TestIdentity(); var context = new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
        var firstSequence = await session.Connection.ExecuteScalarAsync<long>("SELECT COALESCE(MAX(sequence),0) FROM public.inventory_ledger_entry", transaction: transaction);
        var fixture = new Fixture(session, transaction, context, firstSequence);
        try { await action(fixture); Assert.AreSame(transaction, session.Transaction); }
        finally { try { transaction.Rollback(); } finally { session.Transaction = null; } }
    }

    /// <summary>实体字段只为当前证据夹具，所有差异在插入前生成，不更新或删除不可变流水。</summary>
    private sealed class Fixture(DbSession session, IDbTransaction transaction, RepositoryBaseContext context, long sequence)
    {
        public DbSession Session => session;
        public IDbTransaction Transaction => transaction;
        private long _sequence = sequence;
        /// <summary>新头默认完整人工反向身份，源码枚举值保持原定义。</summary>
        public InboundDocument Head() => new() { DocumentNo = "PGH" + Guid.NewGuid().ToString("N"), DocumentType = InboundDocumentTypeEnum.PurchaseReceipt,
            PostingStatus = InboundPostingStatusEnum.Reversed, WarehouseSiteId = Guid.NewGuid(), ReceiptDate = DateTime.UtcNow,
            ReversedDate = DateTime.UtcNow, ReverseReason = "真实反向历史验证" };
        /// <summary>每行只引用随机产品/采购行逻辑身份，不创建额外业务对象或猜测父来源。</summary>
        public InboundDocumentLine Line(InboundDocument head) => new() { InboundDocumentId = head.Uid, ProductId = Guid.NewGuid(), SourceLineId = Guid.NewGuid(),
            LineNumber = 1, Quantity = 5m, SkuSnapshot = "历史验证", UnitSnapshot = "PCS" };
        /// <summary>按现有元数据和打戳新增并保留预生成UID/软删证据；通用Add会重置两者，不适合历史关系夹具。</summary>
        public async Task Add<T>(T entity) where T : BaseEntity
        {
            context.AuditStamp.StampForInsert(entity);
            var metadata = PostgreSqlEntityMetadata.GetMetadata(typeof(T));
            var columns = string.Join(",", metadata.Columns.Select(column => PostgreSqlNames.Quote(column.ColumnName)));
            var parameters = string.Join(",", metadata.Columns.Select(column => "@" + column.ParameterName));
            Assert.AreEqual(1, await session.Connection.ExecuteAsync(new CommandDefinition(
                $"INSERT INTO public.{PostgreSqlNames.Quote(metadata.TableName)} ({columns}) VALUES ({parameters})",
                PostgreSqlParameters.Entity(entity, metadata), transaction)));
        }
        /// <summary>生成恰好一对原/反向流水或明确缺陷，不绕过任何约束和追加触发器。</summary>
        public async Task Pair(InboundDocument head, InboundDocumentLine line, string mode)
        {
            var original = Ledger(head, line, false); original.CommandItemId = line.Uid; original.UnitSnapshot = "pcs ";
            if (mode == "wrongCommandItem") original.CommandItemId = Guid.NewGuid();
            if (mode == "correction") original.CorrectionGroupId = Guid.NewGuid();
            await Add(original); if (mode == "missingReverse") return;
            var reversal = Ledger(head, line, true); reversal.ReversalOfLedgerId = original.Uid;
            if (mode == "wrongQty") reversal.QuantityDelta = -4;
            if (mode == "wrongProduct") reversal.ProductId = Guid.NewGuid();
            if (mode == "wrongWarehouse") reversal.WarehouseSiteId = Guid.NewGuid();
            if (mode == "wrongUnit") reversal.UnitSnapshot = "KG";
            await Add(reversal);
            if (mode == "externalReplacement")
            { var external = Ledger(head, line, false); external.SourceDocumentId = Guid.NewGuid(); external.ReplacementOfLedgerId = original.Uid; await Add(external); }
        }
        /// <summary>显式局部序号保证回滚后序列状态不动，命令项身份除原行外均随机。</summary>
        private InventoryLedgerEntry Ledger(InboundDocument head, InboundDocumentLine line, bool reversed) => new()
        { Sequence = ++_sequence, WarehouseSiteId = head.WarehouseSiteId, ProductId = line.ProductId,
            TransactionType = reversed ? InventoryTransactionTypeEnum.Reversal : head.CreationMode == InboundCreationModeEnum.Automatic ? InventoryTransactionTypeEnum.PackingListAutoReceiptInbound : InventoryTransactionTypeEnum.PurchaseReceiptInbound,
            QuantityDelta = reversed ? -line.Quantity : line.Quantity, UnitSnapshot = "PCS", SourceDocumentType = "inbounddocument ",
            SourceDocumentId = head.Uid, SourceLineId = line.Uid, CommandId = Guid.NewGuid(), CommandItemId = Guid.NewGuid(), OccurredDate = DateTime.UtcNow };
    }
    /// <summary>保留实际服务及实际SQL异常分类；只用窄替身省去尚未实现的Trace其他集合。</summary>
    private sealed class TraceSignal(DbSession session, Guid purchaseLine) : IGrossDemandQueryRepository, IMaterialDemandQueryRepository
    {
        /// <inheritdoc />
        async Task<GrossDemandTraceDetailDto?> IGrossDemandQueryRepository.GetTraceAsync(Guid uid, CancellationToken token)
        { await PostgreSqlInboundReceiptHistoryGuard.ValidateScopedAsync(session, [purchaseLine], token); return null; }
        /// <inheritdoc />
        async Task<MaterialDemandTraceDetailDto?> IMaterialDemandQueryRepository.GetTraceAsync(Guid uid, CancellationToken token)
        { await PostgreSqlInboundReceiptHistoryGuard.ValidateScopedAsync(session, [purchaseLine], token); return null; }
        /// <inheritdoc />
        public Task<PagedResponseBase<GrossDemandListItemDto>> SearchWorkspaceAsync(GrossDemandSearchRequest request, CancellationToken token) => throw new NotSupportedException();
        /// <inheritdoc />
        public Task<IReadOnlyList<GrossDemandSourceGroupDto>> SearchSourceGroupsAsync(GrossDemandSourceGroupSearchRequest request, CancellationToken token) => throw new NotSupportedException();
        /// <inheritdoc />
        public Task<GrossDemandDetailDto?> GetWorkspaceDetailAsync(Guid uid, CancellationToken token) => throw new NotSupportedException();
        /// <inheritdoc />
        public Task<PagedResponseBase<MaterialDemandManagerItemDto>> SearchManagerAsync(MaterialDemandManagerSearchRequest request, CancellationToken token) => throw new NotSupportedException();
        /// <inheritdoc />
        public Task<IReadOnlyList<NetDemandSourceGroupDto>> SearchMaterialSourceGroupsAsync(MaterialDemandSourceGroupSearchRequest request, CancellationToken token) => throw new NotSupportedException();
    }
    /// <summary>固定测试审计名，不代表真实登录账号。</summary>
    private sealed class TestIdentity : ICurrentUser
    { public Guid? UserId => null; public string UserName => "PG历史门禁测试"; public bool IsAuthenticated => false; }
}
