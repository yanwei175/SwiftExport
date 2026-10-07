using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Execution;
using SwiftExport.Infrastructure.Services.Auth;
using System.Buffers.Binary;
using System.Data;
using System.Data.SqlTypes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SwiftExport.Application.Tests.Database;

/// <summary>独立原生连接验证四方法和事务前锁；只允许固定failure库，不初始化库或消耗业务号。</summary>
[TestClass, DoNotParallelize]
public sealed class PostgreSqlNetDemandExecutionLinkMigrationIntegrationTests
{
    /// <summary>输出随机UID清单，供主控核对提交和精确清理范围。</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>合法0/8/9全字段读回，取消/反向仍查重，只有未释放贡献进入两种Net读。</summary>
    [TestMethod]
    public async Task FourMethods_PreserveContributionHistoryAndFullProjection()
    {
        await using var fixture = await Create(); using var session = fixture.Open(); var repo = Repo(session);
        var active = fixture.Link(0); active.ReleasedQty = 2; active.Version = 7;
        active.DemandRequiredDate = new DateTime(2026, 10, 6); active.PlannedDeliveryDate = new DateTime(2026, 10, 7);
        active.Remark = "原快照";
        var cancelled = fixture.Link(0); cancelled.LinkStatus = (DemandExecutionLinkStatusEnum)8;
        cancelled.ReleasedQty = 10; cancelled.CancelledAt = new DateTime(2026, 10, 5); cancelled.CancelledBy = "NL取消"; cancelled.CancelledReason = "保留历史";
        var reversed = fixture.Link(0); reversed.LinkStatus = (DemandExecutionLinkStatusEnum)9;
        reversed.ReleasedQty = 10; reversed.ReversedAt = new DateTime(2026, 10, 5); reversed.ReversedBy = "NL反向"; reversed.ReversedReason = "保留身份";
        var links = new[] { active, cancelled, reversed };
        // PG时间列为微秒精度，完整投影断言使用可精确表示的输入，避免审计时钟100ns舍入误判。
        foreach (var link in links) link.CreatedDate = link.UpdatedDate = new DateTime(2026, 10, 5, 9, 0, 0);
        await using var scope = await Scope.Begin(session, repo, [fixture.Nets[0].Uid], links.Select(x => x.CommandItemId).ToArray());
        Assert.AreEqual(3, await repo.InsertPurchaseLinksAsync(links));
        var contributions = await repo.GetActiveByNetDemandIdsForUpdateAsync([fixture.Nets[0].Uid, fixture.Nets[0].Uid]);
        Assert.AreEqual(1, contributions.Count); Assert.AreEqual(8m, contributions[0].AllocatedQty);
        var blockers = await repo.GetRollbackBlockersAsync([fixture.Nets[0].Uid]);
        Assert.AreEqual(1, blockers.Count);
        Assert.AreEqual($"净需求已关联 PO {fixture.Head.OrderNo}，请先取消或完成反向处理。", blockers[0].Message);
        var rows = await repo.GetByCommandItemIdsForUpdateAsync(links.Select(x => x.CommandItemId).Append(Guid.Empty));
        Assert.AreEqual(3, rows.Count);
        CollectionAssert.AreEqual(links.OrderBy(x => new SqlGuid(x.CommandItemId)).ThenBy(x => new SqlGuid(x.Uid)).Select(x => x.Uid).ToArray(), rows.Select(x => x.Uid).ToArray());
        foreach (var expected in links)
        {
            var actual = rows.Single(x => x.Uid == expected.Uid);
            foreach (var column in EntityMetadataCache.GetMetadata(typeof(DemandExecutionLink)).Columns)
                Assert.AreEqual(column.Property.GetValue(expected), column.Property.GetValue(actual), column.PropertyName);
        }
    }

    /// <summary>同日期两行按SqlGuid排序，Net空范围和缺失UID仍正常空结果。</summary>
    [TestMethod]
    public async Task ActiveOrderingAndEmptyRange_ArePreserved()
    {
        await using var fixture = await Create(); using var session = fixture.Open(); var repo = Repo(session);
        var first = fixture.Link(0); var second = fixture.Link(0); var nullDate = fixture.Link(0);
        first.CreatedDate = second.CreatedDate = new DateTime(2026, 10, 5);
        await using var scope = await Scope.Begin(session, repo, fixture.Nets.Select(x => x.Uid).ToArray(), [first.CommandItemId, second.CommandItemId, nullDate.CommandItemId]);
        Assert.AreEqual(0, (await repo.GetActiveByNetDemandIdsForUpdateAsync([fixture.Nets[1].Uid])).Count);
        Assert.AreEqual(0, (await repo.GetRollbackBlockersAsync([Guid.NewGuid()])).Count);
        Assert.AreEqual(2, await repo.InsertPurchaseLinksAsync([first, second]));
        await Fixture.Insert(session, nullDate);
        var rows = await repo.GetActiveByNetDemandIdsForUpdateAsync([fixture.Nets[0].Uid]);
        CollectionAssert.AreEqual(new[] { nullDate.Uid }.Concat(new[] { first, second }.OrderBy(x => new SqlGuid(x.Uid)).Select(x => x.Uid)).ToArray(), rows.Select(x => x.Uid).ToArray());
    }

    /// <summary>原空CommandItem写入仍合法且参与真实身份准备，读取Empty仍免SQL；不清已有全局空身份。</summary>
    [TestMethod]
    public async Task EmptyCommandItem_IsPreparedForWriteAndFilteredForRead()
    {
        await using var fixture = await Create(); using var session = fixture.Open(); var repo = Repo(session);
        if (await session.Connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM public.demand_execution_link WHERE command_item_id=@Item)", new { Item = Guid.Empty }))
            Assert.Inconclusive("库已有全局Empty命令项；本例不清理或覆盖既有身份。");
        var link = fixture.Link(0, Guid.Empty);
        await using var scope = await Scope.Begin(session, repo, [link.NetDemandId], [Guid.Empty]);
        Assert.IsTrue(session.SourceDemandLease!.Covers("CommandItem", Guid.Empty));
        Assert.AreEqual(1, await repo.InsertPurchaseLinksAsync([link]));
        Assert.AreEqual(0, (await repo.GetByCommandItemIdsForUpdateAsync([Guid.Empty])).Count);
        Assert.AreEqual(link.Uid, (await repo.GetActiveByNetDemandIdsForUpdateAsync([link.NetDemandId])).Single().Uid);
    }

    /// <summary>无父四方法各自真实准备并归还，不建立隐式业务事务；历史取消关系仍可读取。</summary>
    [TestMethod]
    public async Task NoParent_AllFourMethods_ReturnTheirLocalLease()
    {
        await using var fixture = await Create(); using var session = fixture.Open(); var repo = Repo(session);
        var active = fixture.Link(0); var cancelled = fixture.Link(0);
        cancelled.LinkStatus = (DemandExecutionLinkStatusEnum)8; cancelled.ReleasedQty = 10;
        Assert.AreEqual(2, await repo.InsertPurchaseLinksAsync([active, cancelled]));
        Assert.IsNull(session.Transaction); Assert.IsNull(session.SourceDemandLease);
        Assert.AreEqual(1, (await repo.GetActiveByNetDemandIdsForUpdateAsync([active.NetDemandId])).Count);
        Assert.IsNull(session.Transaction); Assert.IsNull(session.SourceDemandLease);
        Assert.AreEqual(1, (await repo.GetRollbackBlockersAsync([active.NetDemandId])).Count);
        Assert.IsNull(session.Transaction); Assert.IsNull(session.SourceDemandLease);
        Assert.AreEqual(cancelled.Uid, (await repo.GetByCommandItemIdsForUpdateAsync([cancelled.CommandItemId])).Single().Uid);
        Assert.IsNull(session.Transaction); Assert.IsNull(session.SourceDemandLease);
    }

    /// <summary>逐条Dapper第二条全局Item冲突：无父首行已提交，有父回滚后两行均不存在。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DuplicateSecondRow_PreservesOriginalAtomicity(bool parent)
    {
        await using var fixture = await Create(); using var session = fixture.Open(); var repo = Repo(session);
        var first = fixture.Link(0); var second = fixture.Link(1, first.CommandItemId);
        Scope? scope = parent ? await Scope.Begin(session, repo, fixture.Nets.Select(x => x.Uid).ToArray(), [first.CommandItemId]) : null;
        try
        {
            var error = await Assert.ThrowsExactlyAsync<PostgresException>(() => repo.InsertPurchaseLinksAsync([first, second]));
            Assert.AreEqual("23505", error.SqlState);
            Assert.IsNotNull(first.CreatedDate); Assert.IsNotNull(second.CreatedDate);
            Assert.AreEqual(1, first.Version); Assert.AreEqual(1, second.Version);
        }
        finally { if (scope is not null) await scope.DisposeAsync(); }
        using var observer = fixture.Open();
        Assert.AreEqual(parent ? 0 : 1, await fixture.CountLinks(observer));
        if (!parent) Assert.IsNull(session.SourceDemandLease);
    }

    /// <summary>非法状态由真实CHECK拒绝，仓储不提前增加业务预校验或吞数据库错误。</summary>
    [TestMethod]
    [DataRow(77, 0)]
    [DataRow(8, 0)]
    [DataRow(0, 10)]
    public async Task InvalidState_IsRejectedByDatabaseAfterStamp(int status, int released)
    {
        await using var fixture = await Create(); using var session = fixture.Open(); var repo = Repo(session);
        var link = fixture.Link(0); link.LinkStatus = (DemandExecutionLinkStatusEnum)status; link.ReleasedQty = released;
        await using var scope = await Scope.Begin(session, repo, [link.NetDemandId], [link.CommandItemId]);
        var error = await Assert.ThrowsExactlyAsync<PostgresException>(() => repo.InsertPurchaseLinksAsync([link]));
        Assert.AreEqual("23514", error.SqlState); Assert.IsNotNull(link.CreatedDate); Assert.AreEqual(1, link.Version);
    }

    /// <summary>合法PO消息原样；SQL字典尾空格与NULL CONCAT用真实表达式验证，非法行由当前CHECK拒绝。</summary>
    [TestMethod]
    public async Task DictionaryAndDatabaseNullConcat_AreObservedWithoutConstraintChanges()
    {
        await using var fixture = await Create(); using var session = fixture.Open(); var repo = Repo(session);
        var link = fixture.Link(0); var illegal = fixture.Link(0); illegal.TargetType = "po ";
        await using var scope = await Scope.Begin(session, repo, [link.NetDemandId], [link.CommandItemId, illegal.CommandItemId]);
        Assert.AreEqual(1, await repo.InsertPurchaseLinksAsync([link]));
        Assert.AreEqual(1, (await repo.GetActiveByNetDemandIdsForUpdateAsync([link.NetDemandId])).Count);
        Assert.AreEqual($"净需求已关联 PO {fixture.Head.OrderNo}，请先取消或完成反向处理。",
            (await repo.GetRollbackBlockersAsync([link.NetDemandId])).Single().Message);
        Assert.IsTrue(await session.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT rtrim('po ',' ') COLLATE public.erp_dictionary_ci = rtrim('PO',' ') COLLATE public.erp_dictionary_ci", transaction: session.Transaction)));
        Assert.AreEqual("净需求已关联  ，请先取消或完成反向处理。", await session.Connection.ExecuteScalarAsync<string>(
            new CommandDefinition("SELECT concat('净需求已关联 ',NULL::text,' ',NULL::text,'，请先取消或完成反向处理。')", transaction: session.Transaction)));
        var error = await Assert.ThrowsExactlyAsync<PostgresException>(() => Fixture.Insert(session, illegal));
        Assert.AreEqual("23514", error.SqlState);
    }

    /// <summary>只消费父事务真实已持覆盖；缺租约或准备另一Gross不能在业务快照后补锁。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MissingOrIncompleteParentCoverage_IsRejected(bool incomplete)
    {
        await using var fixture = await Create(); using var session = fixture.Open(); var repo = Repo(session);
        if (incomplete)
        {
            await using var scope = await Scope.Begin(session, repo, [fixture.Nets[0].Uid], []);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repo.GetActiveByNetDemandIdsForUpdateAsync([fixture.Nets[1].Uid]));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repo.GetByCommandItemIdsForUpdateAsync([Guid.NewGuid()]));
        }
        else
        {
            using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted); session.Transaction = transaction;
            try { await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repo.GetRollbackBlockersAsync([fixture.Nets[0].Uid])); }
            finally { transaction.Rollback(); session.Transaction = null; }
        }
    }

    /// <summary>租约归还后不能被父事务借用；开闭周期变化使原真实租约失效，不伪造Held集合。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DisposedOrChangedConnectionLease_IsRejected(bool reopen)
    {
        await using var fixture = await Create(); using var session = fixture.Open(); var repo = Repo(session);
        var lease = await repo.AcquireMaterialBeforeTransactionAsync([], [], [fixture.Nets[0].Uid], []);
        Assert.IsNotNull(lease);
        try
        {
            if (reopen) { session.Connection.Close(); session.Connection.Open(); }
            else await lease.DisposeAsync();
            using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted); session.Transaction = transaction;
            try { await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repo.GetActiveByNetDemandIdsForUpdateAsync([fixture.Nets[0].Uid])); }
            finally { transaction.Rollback(); session.Transaction = null; }
        }
        finally { await lease.DisposeAsync(); }
    }

    /// <summary>已准备父事务中的取消传播原token；空Item输入仍忽略取消免SQL。</summary>
    [TestMethod]
    public async Task Cancellation_IsPropagatedAfterPreparation()
    {
        await using var fixture = await Create(); using var session = fixture.Open(); var repo = Repo(session); var item = Guid.NewGuid();
        await using var scope = await Scope.Begin(session, repo, [fixture.Nets[0].Uid], [item]);
        var token = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => repo.GetByCommandItemIdsForUpdateAsync([item], token));
        Assert.AreEqual(0, (await repo.GetByCommandItemIdsForUpdateAsync([Guid.Empty], token)).Count);
    }

    /// <summary>两真实backend同Net空范围准备互斥；前者提交贡献后，后者RC首读看见原净贡献。</summary>
    [TestMethod]
    public async Task TwoBackends_SameNetEmptyRange_ObserveCommittedContribution()
    {
        await using var fixture = await Create(); using var owner = fixture.Open(); using var contender = fixture.Open(); using var observer = fixture.Open();
        var firstRepo = Repo(owner); var secondRepo = Repo(contender); var link = fixture.Link(0);
        await using var scope = await Scope.Begin(owner, firstRepo, [link.NetDemandId], [link.CommandItemId]);
        Assert.AreEqual(0, (await firstRepo.GetActiveByNetDemandIdsForUpdateAsync([link.NetDemandId])).Count);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var pending = secondRepo.AcquireMaterialBeforeTransactionAsync([], [], [link.NetDemandId], [], timeout.Token);
        try
        {
            await ObserveAttempt(observer, contender, owner, "Source", fixture.Gross[0].SourceUid, timeout.Token);
            Assert.IsFalse(pending.IsCompleted); Assert.IsNull(contender.Transaction);
            Assert.AreEqual(1, await firstRepo.InsertPurchaseLinksAsync([link]));
            await scope.Commit();
            await using var acquired = await pending; Assert.IsNotNull(acquired);
            using var transaction = contender.Connection.BeginTransaction(IsolationLevel.ReadCommitted); contender.Transaction = transaction;
            try { Assert.AreEqual(10m, (await secondRepo.GetActiveByNetDemandIdsForUpdateAsync([link.NetDemandId])).Single().AllocatedQty); }
            finally { transaction.Rollback(); contender.Transaction = null; }
        }
        finally { await FinishPending(pending, scope); }
    }

    /// <summary>跨Source同Item全局争用；取消已提交身份仍被发现，后者23505且不能分源重用。</summary>
    [TestMethod]
    public async Task TwoBackends_CrossSourceSameItem_CancelledIdentityRemainsGlobal()
    {
        await using var fixture = await Create(); using var owner = fixture.Open(); using var contender = fixture.Open(); using var observer = fixture.Open();
        var firstRepo = Repo(owner); var secondRepo = Repo(contender);
        var first = fixture.Link(0); first.LinkStatus = (DemandExecutionLinkStatusEnum)8; first.ReleasedQty = first.AllocatedQty;
        var second = fixture.Link(1, first.CommandItemId);
        await using var scope = await Scope.Begin(owner, firstRepo, [first.NetDemandId], [first.CommandItemId]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var pending = secondRepo.AcquireMaterialBeforeTransactionAsync([], [], [second.NetDemandId], [second.CommandItemId], timeout.Token);
        try
        {
            await ObserveAttempt(observer, contender, owner, "CommandItem", first.CommandItemId, timeout.Token);
            Assert.IsFalse(pending.IsCompleted); Assert.IsNull(contender.Transaction);
            Assert.AreEqual(1, await firstRepo.InsertPurchaseLinksAsync([first])); await scope.Commit();
            await using var acquired = await pending; Assert.IsNotNull(acquired);
            using var transaction = contender.Connection.BeginTransaction(IsolationLevel.ReadCommitted); contender.Transaction = transaction;
            try
            {
                var history = await secondRepo.GetByCommandItemIdsForUpdateAsync([first.CommandItemId]);
                Assert.AreEqual(first.Uid, history.Single().Uid); Assert.AreEqual((DemandExecutionLinkStatusEnum)8, history[0].LinkStatus);
                var error = await Assert.ThrowsExactlyAsync<PostgresException>(() => secondRepo.InsertPurchaseLinksAsync([second]));
                Assert.AreEqual("23505", error.SqlState);
            }
            finally { transaction.Rollback(); contender.Transaction = null; }
            Assert.AreEqual(1, await fixture.CountLinks(observer));
        }
        finally { await FinishPending(pending, scope); }
    }

    /// <summary>门禁在任何连接之前执行，未设置明确failure授权便Inconclusive。</summary>
    private async Task<Fixture> Create()
    {
        var text = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        var settings = new NpgsqlConnectionStringBuilder(text);
        Assert.IsFalse(settings.Multiplexing); Assert.IsFalse(settings.NoResetOnClose);
        var fixture = new Fixture(text, json => TestContext.WriteLine(json));
        try { await fixture.Initialize(); return fixture; }
        catch { await fixture.DisposeAsync(); throw; }
    }
    /// <summary>同context真实PG仓储、当前用户与审计服务，不依赖A的Net实现。</summary>
    private static PostgreSqlNetDemandExecutionLinkRepository Repo(DbSession session)
    {
        var user = new User(); return new(new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user));
    }
    /// <summary>目录确认持有者的实际long键和候选真实try-lock查询，不能以Delay或Task启动代替取证。</summary>
    private static async Task ObserveAttempt(DbSession observer, DbSession candidate, DbSession owner, string family, Guid uid, CancellationToken token)
    {
        Assert.AreNotEqual(((NpgsqlConnection)owner.Connection).ProcessID, ((NpgsqlConnection)candidate.Connection).ProcessID);
        var key = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"SwiftExport:SourceDemand:{family}:{uid:N}")));
        var bits = unchecked((ulong)key);
        var parameters = new { Candidate = ((NpgsqlConnection)candidate.Connection).ProcessID, Owner = ((NpgsqlConnection)owner.Connection).ProcessID,
            High = (long)(bits >> 32), Low = (long)(bits & uint.MaxValue) };
        while (!await observer.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid=@Candidate AND backend_xid IS NULL
                           AND query LIKE '%pg_try_advisory_lock%')
               AND EXISTS(SELECT 1 FROM pg_locks WHERE pid=@Owner AND locktype='advisory' AND granted
                           AND classid::bigint=@High AND objid::bigint=@Low AND objsubid=1)
            """, parameters, cancellationToken: token))) await Task.Delay(20, token);
    }
    /// <summary>先结束owner父事务与租约，再等待并释放候选，失败也不能把租约遗留到池中。</summary>
    private static async Task FinishPending(Task<IAsyncDisposable?> pending, Scope scope)
    {
        await scope.DisposeAsync();
        try { var lease = await pending; if (lease is not null) await lease.DisposeAsync(); }
        catch (OperationCanceledException) { /* 测试超时已有原失败，清理不替代其诊断。 */ }
    }
    /// <summary>RC父事务始终在真实整组准备完成之后开始，默认回滚且随后归还租约。</summary>
    private sealed class Scope(DbSession session, IAsyncDisposable lease, IDbTransaction transaction) : IAsyncDisposable
    {
        private bool ended;
        /// <summary>保留同一会话准备能力，缺失返回不当作成功。</summary>
        internal static async Task<Scope> Begin(DbSession session, PostgreSqlNetDemandExecutionLinkRepository repo, Guid[] nets, Guid[] items)
        {
            var lease = await repo.AcquireMaterialBeforeTransactionAsync([], [], nets, items);
            Assert.IsNotNull(lease);
            try { var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted); session.Transaction = transaction; return new(session, lease, transaction); }
            catch { await lease.DisposeAsync(); throw; }
        }
        /// <summary>只有两连接/原autocommit对照场景显式提交，UID已在fixture预登记。</summary>
        internal async Task Commit() { transaction.Commit(); session.Transaction = null; ended = true; transaction.Dispose(); await lease.DisposeAsync(); }
        /// <summary>父事务先回滚结束，再释放来源/Item；保留数据库失败原诊断。</summary>
        public async ValueTask DisposeAsync()
        {
            if (ended) return;
            try { transaction.Rollback(); }
            finally { session.Transaction = null; ended = true; transaction.Dispose(); await lease.DisposeAsync(); }
        }
    }
    /// <summary>仅五业务表的随机合法逻辑引用夹具；已有供应商只读，不新增角色、号、回执或结构。</summary>
    private sealed class Fixture(string connection, Action<string> write) : IAsyncDisposable
    {
        internal GrossDemand[] Gross = [];
        internal NetDemand[] Nets = [];
        internal PurchaseOrderHead Head = new();
        private PurchaseOrderLine line = new();
        private readonly List<Guid> links = [];
        private bool initialized;
        /// <summary>每次创建独立原生物理连接，由生产DbSession管理epoch和租约。</summary>
        internal DbSession Open() => new(new PostgreSqlConnectionFactory(connection));
        /// <summary>提交前预登记全部基础UID，允许跨连接发现；不足供应商时不造悬空引用。</summary>
        internal async Task Initialize()
        {
            using var session = Open();
            var supplier = await session.Connection.QueryFirstOrDefaultAsync<SupplierSnapshot>(
                "SELECT uid AS \"Uid\",supplier_code AS \"Code\",supplier_name AS \"Name\" FROM public.supplier WHERE is_deleted=FALSE AND is_active=TRUE ORDER BY uid LIMIT 1");
            if (supplier is null) Assert.Inconclusive("专用库无合法供应商；不创建悬空采购引用或改主数据。");
            Gross = Enumerable.Range(0, 2).Select(_ =>
            {
                var uid = Guid.NewGuid(); return new GrossDemand { Uid = uid, Version = 1, SourceType = "MD", SourceUid = uid, SourceLineId = uid,
                    SourceNo = "NL-" + uid.ToString("N"), IsNonStandard = true, SkuSnapshot = "NL夹具", DemandQty = 100, UnitSnapshot = "PCS" };
            }).ToArray();
            Nets = Gross.Select(gross =>
            {
                var uid = Guid.NewGuid(); return new NetDemand { Uid = uid, Version = 1, GrossDemandId = gross.Uid, RootNetDemandId = uid,
                    DecompositionRevision = 1, LineNumber = 1, SupplyTypeSnapshot = "采购", SkuSnapshot = "NL夹具", GrossQty = 100, NetQty = 100, UnitSnapshot = "PCS" };
            }).ToArray();
            Head = new() { Uid = Guid.NewGuid(), Version = 1, OriginKind = 1, OrderNo = "NL-" + Guid.NewGuid().ToString("N"),
                ExecutionRequestId = Guid.NewGuid(), SupplierId = supplier!.Uid, SupplierCode = supplier.Code, SupplierName = supplier.Name,
                OrderDate = new DateTime(2026, 10, 5), Currency = "CNY" };
            line = new() { Uid = Guid.NewGuid(), Version = 1, PurchaseOrderId = Head.Uid, LineNumber = 1, OrderNo = Head.OrderNo,
                Sku = "NL夹具", Quantity = 100, SupplyType = "采购", UnitSnapshot = "PCS" };
            write(JsonSerializer.Serialize(new { Phase = "准备基础UID", Gross = Gross.Select(x => x.Uid), Net = Nets.Select(x => x.Uid), Head = Head.Uid, Line = line.Uid }));
            using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted); session.Transaction = transaction;
            try
            {
                foreach (var gross in Gross) await Insert(session, gross);
                foreach (var net in Nets) await Insert(session, net);
                await Insert(session, Head); await Insert(session, line); transaction.Commit(); initialized = true;
            }
            finally { session.Transaction = null; }
        }
        /// <summary>所有潜在提交Link在实际SQL之前登记，失败时也精确清理本例UID。</summary>
        internal DemandExecutionLink Link(int net, Guid? item = null)
        {
            var uid = Guid.NewGuid(); links.Add(uid);
            write(JsonSerializer.Serialize(new { Phase = "预登记Link", Uid = uid }));
            return new() { Uid = uid, NetDemandId = Nets[net].Uid, ExecutionRequestId = Head.ExecutionRequestId, CommandItemId = item ?? Guid.NewGuid(),
                TargetType = "PO", TargetHeadId = Head.Uid, TargetNo = Head.OrderNo, TargetLineId = line.Uid, AllocatedQty = 10 };
        }
        /// <summary>typed元数据夹具写入只借用显式父事务，不修改约束或调用未交付Net仓储。</summary>
        internal static Task<int> Insert(DbSession session, BaseEntity entity)
        {
            var map = EntityMetadataCache.GetMetadata(entity.GetType());
            var sql = $"INSERT INTO {PostgreSqlNames.Quote(map.SchemaName)}.{PostgreSqlNames.Quote(map.TableName)} ({string.Join(',', map.Columns.Select(x => PostgreSqlNames.Quote(x.ColumnName)))}) VALUES ({string.Join(',', map.Columns.Select(x => '@' + x.ParameterName))})";
            return session.Connection.ExecuteAsync(new CommandDefinition(sql, new Parameters(entity, map), session.Transaction));
        }
        /// <summary>独立连接只数本例预登记身份，不将全库行数视为本批成果。</summary>
        internal Task<int> CountLinks(DbSession session) => session.Connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM public.demand_execution_link WHERE uid=ANY(@Uids)", PostgreSqlParameters.Ids(links));
        /// <summary>只有初始化确已提交才清理本例UID，顺序Link→Net→Gross与采购行→头；不清全库。</summary>
        public async ValueTask DisposeAsync()
        {
            if (!initialized) return;
            using var session = Open(); using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted); session.Transaction = transaction;
            try
            {
                await session.Connection.ExecuteAsync(new CommandDefinition("DELETE FROM public.demand_execution_link WHERE uid=ANY(@Uids)", PostgreSqlParameters.Ids(links), transaction));
                await session.Connection.ExecuteAsync(new CommandDefinition("DELETE FROM public.net_demand WHERE uid=ANY(@Uids)", PostgreSqlParameters.Ids(Nets.Select(x => x.Uid)), transaction));
                await session.Connection.ExecuteAsync(new CommandDefinition("DELETE FROM public.gross_demand WHERE uid=ANY(@Uids)", PostgreSqlParameters.Ids(Gross.Select(x => x.Uid)), transaction));
                await session.Connection.ExecuteAsync(new CommandDefinition("DELETE FROM public.purchase_order_line WHERE uid=@Uid", new { line.Uid }, transaction));
                await session.Connection.ExecuteAsync(new CommandDefinition("DELETE FROM public.purchase_order_head WHERE uid=@Uid", new { Head.Uid }, transaction));
                transaction.Commit(); initialized = false;
                write(JsonSerializer.Serialize(new { Phase = "精确UID清理已提交", Links = links, Gross = Gross.Select(x => x.Uid), Net = Nets.Select(x => x.Uid), Head = Head.Uid, Line = line.Uid }));
            }
            finally { session.Transaction = null; }
        }
    }
    /// <summary>只读取既有供应商建立真实采购快照，不依赖当前库新建主数据。</summary>
    private sealed class SupplierSnapshot
    {
        public Guid Uid { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
    /// <summary>夹具text与日期显式typed，与生产实体列元数据一致，不增加生产测试入口。</summary>
    private sealed class Parameters(BaseEntity entity, EntityMetadata map) : SqlMapper.IDynamicParameters
    {
        /// <summary>给当前独立命令绑定克隆参数，NULL文本明确text。</summary>
        public void AddParameters(IDbCommand command, SqlMapper.Identity identity)
        {
            PostgreSqlParameters.Entity(entity, map).AddParameters(command, identity);
            foreach (var column in map.Columns.Where(x => x.Property.PropertyType == typeof(string)))
                ((NpgsqlParameter)command.Parameters[column.ParameterName]!).NpgsqlDbType = NpgsqlDbType.Text;
        }
    }
    /// <summary>专用随机测试操作者只用于审计，不创建真实账号或权限关系。</summary>
    private sealed class User : ICurrentUser
    {
        /// <summary>固定可辨识审计名称。</summary>
        public string UserName => "M15-NL-IMPL01";
        /// <summary>不冒用真实账号身份。</summary>
        public Guid? UserId => null;
        /// <summary>仓储测试不声称认证链验收。</summary>
        public bool IsAuthenticated => false;
    }
}
