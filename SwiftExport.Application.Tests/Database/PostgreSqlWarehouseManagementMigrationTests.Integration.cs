using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Inventory.Warehouses;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>仅在保留专用库验证仓库管理；普通场景全部回滚，并发场景只提交及精确清理本例供应商和仓库。</summary>
[TestClass]
public sealed class PostgreSqlWarehouseManagementMigrationIntegrationTests
{
    /// <summary>完整投影、权威供应商、审计、版本及软删除保持原管理合同，停用记录不能被读取过滤丢弃。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlMasterDataIntegration")]
    public async Task Management_LifecycleAndAllFieldsShareRollbackTransaction()
    {
        using var fixture = new Fixture();
        fixture.BeginA();
        try
        {
            var supplier = await fixture.AddSupplier(fixture.A, "S");
            var inactive = await fixture.AddSupplier(fixture.A, "I", active: false);
            var deleted = await fixture.AddSupplier(fixture.A, "D", deleted: true);
            var identity = await fixture.RepoA.GetActiveSupplierAsync(supplier.Uid);
            Assert.IsNotNull(identity);
            Assert.AreEqual(supplier.SupplierCode, identity.SupplierCode);
            Assert.AreEqual(supplier.SupplierName, identity.SupplierName);
            Assert.IsNull(await fixture.RepoA.GetActiveSupplierAsync(inactive.Uid));
            Assert.IsNull(await fixture.RepoA.GetActiveSupplierAsync(deleted.Uid));
            Assert.IsNull(await fixture.RepoA.GetActiveSupplierAsync(Guid.NewGuid()));
            var site = fixture.Site("W", supplier);
            Assert.AreEqual(1, await fixture.RepoA.InsertAsync(site));
            var row = await fixture.RepoA.GetForUpdateAsync(site.Uid);
            Assert.IsNotNull(row);
            Assert.AreEqual(site.Code, row.Code);
            Assert.AreEqual(site.Name, row.Name);
            Assert.AreEqual(site.SiteType, row.SiteType);
            Assert.AreEqual(supplier.Uid, row.SupplierId);
            Assert.AreEqual(site.SupplierCodeSnapshot, row.SupplierCodeSnapshot);
            Assert.AreEqual(site.SupplierNameSnapshot, row.SupplierNameSnapshot);
            Assert.AreEqual(fixture.Actor, row.CreatedBy);
            Assert.IsNotNull(row.CreatedDate);
            Assert.AreEqual(1, row.Version);
            Assert.IsTrue(row.IsActive);
            Assert.IsFalse(row.IsDeleted);
            Assert.AreEqual(site.Uid, (await fixture.RepoA.FindByCodeForUpdateAsync(site.Code.ToLowerInvariant() + " "))!.Uid);
            Assert.AreEqual(site.Uid, (await fixture.RepoA.FindActiveSupplierWarehouseForUpdateAsync(supplier.Uid))!.Uid);
            Assert.IsNull(await fixture.RepoA.FindByCodeForUpdateAsync(fixture.Marker + "ABSENT"));
            row.Name = "本例修改名称";
            Assert.AreEqual(0, await fixture.RepoA.UpdateNameAsync(row, 99));
            Assert.AreEqual(1, await fixture.RepoA.UpdateNameAsync(row, 1));
            row = (await fixture.RepoA.GetForUpdateAsync(site.Uid))!;
            Assert.AreEqual(2, row.Version);
            Assert.AreEqual("本例修改名称", row.Name);
            Assert.AreEqual(fixture.Actor, row.UpdatedBy);
            Assert.IsNotNull(row.UpdatedDate);
            Assert.AreEqual(0, await fixture.RepoA.DeactivateAsync(row, 1));
            Assert.AreEqual(1, await fixture.RepoA.DeactivateAsync(row, 2));
            row = (await fixture.RepoA.GetForUpdateAsync(site.Uid))!;
            Assert.AreEqual(3, row.Version);
            Assert.IsFalse(row.IsActive);
            Assert.IsNull(await fixture.RepoA.FindActiveSupplierWarehouseForUpdateAsync(supplier.Uid));
            Assert.AreEqual(0, await fixture.RepoA.ActivateAsync(row, 2));
            Assert.AreEqual(1, await fixture.RepoA.ActivateAsync(row, 3));
            row = (await fixture.RepoA.GetForUpdateAsync(site.Uid))!;
            Assert.AreEqual(4, row.Version);
            Assert.IsTrue(row.IsActive);
            Assert.AreEqual(0, await fixture.RepoA.DeleteAsync(site.Uid, 3));
            Assert.AreEqual(1, await fixture.RepoA.DeleteAsync(site.Uid, 4));
            var removed = await fixture.ReadSite(fixture.A, site.Uid);
            Assert.IsTrue(removed.IsDeleted);
            Assert.IsFalse(removed.IsActive);
            Assert.AreEqual(5, removed.Version);
            Assert.AreEqual(fixture.Actor, removed.UpdatedBy);
            Assert.AreEqual(row.CreatedBy, removed.CreatedBy);
            Assert.AreEqual(row.CreatedDate, removed.CreatedDate);
            Assert.AreEqual(row.SupplierNameSnapshot, removed.SupplierNameSnapshot);
            Assert.IsNull(await fixture.RepoA.GetForUpdateAsync(site.Uid));
            Assert.IsNull(await fixture.RepoA.FindByCodeForUpdateAsync(site.Code));
        }
        finally { fixture.RollbackA(); }
        await fixture.AssertSitesAbsent();
    }

    /// <summary>非零余额阻断删除，零余额允许删除；历史流水仅影响停用查询，不提交或删除任何流水。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlMasterDataIntegration")]
    public async Task Management_BalanceAndHistoryKeepDifferentBoundaries()
    {
        using var fixture = new Fixture();
        fixture.BeginA();
        var ledgerId = Guid.NewGuid();
        try
        {
            var supplier = await fixture.AddSupplier(fixture.A, "S");
            var site = fixture.Site("W", supplier);
            Assert.AreEqual(1, await fixture.RepoA.InsertAsync(site));
            Assert.IsNotNull(await fixture.RepoA.GetForUpdateAsync(site.Uid));
            Assert.HasCount(0, await fixture.RepoA.GetDeleteBlockersAsync(site.Uid));
            Assert.IsFalse(await fixture.RepoA.HasBalanceOrLedgerAsync(site.Uid));
            var balance = new InventoryBalance { Uid = Guid.NewGuid(), WarehouseSiteId = site.Uid,
                ProductId = Guid.NewGuid(), OnHandQty = 5, UnitSnapshot = "PCS" };
            await fixture.Insert(fixture.A, balance);
            CollectionAssert.AreEqual(new[] { "库存余额非零" }, (await fixture.RepoA.GetDeleteBlockersAsync(site.Uid)).ToArray());
            Assert.IsTrue(await fixture.RepoA.HasBalanceOrLedgerAsync(site.Uid));
            Assert.AreEqual(1, await fixture.A.Session.Connection.ExecuteAsync(new CommandDefinition(
                "UPDATE public.inventory_balance SET on_hand_qty=0 WHERE uid=@Uid AND created_by=@Actor",
                new { balance.Uid, fixture.Actor }, fixture.A.Session.Transaction, cancellationToken: fixture.Token)));
            Assert.HasCount(0, await fixture.RepoA.GetDeleteBlockersAsync(site.Uid));
            Assert.IsFalse(await fixture.RepoA.HasBalanceOrLedgerAsync(site.Uid));
            // 本例流水只存在于未提交事务。随机正序号避免消耗公共 sequence；绝不 UPDATE/DELETE 流水。
            var ledger = new InventoryLedgerEntry { Uid = ledgerId, Sequence = Random.Shared.NextInt64(1_000_000_000_000, long.MaxValue),
                WarehouseSiteId = site.Uid, ProductId = balance.ProductId, TransactionType = InventoryTransactionTypeEnum.OpeningBalance,
                QuantityDelta = 1, BalanceAfter = 1, CommandId = Guid.NewGuid(), CommandItemId = Guid.NewGuid(), OccurredDate = DateTime.Now,
                SourceDocumentType = "本例回滚夹具", WarehouseCodeSnapshot = site.Code, UnitSnapshot = "PCS" };
            await fixture.Insert(fixture.A, ledger);
            Assert.IsTrue(await fixture.RepoA.HasBalanceOrLedgerAsync(site.Uid));
            Assert.HasCount(0, await fixture.RepoA.GetDeleteBlockersAsync(site.Uid));
            Assert.AreEqual(1, await fixture.RepoA.DeleteAsync(site.Uid, 1));
        }
        finally { fixture.RollbackA(); }
        Assert.AreEqual(0, await fixture.Observer.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM public.inventory_ledger_entry WHERE uid=@Uid", new { Uid = ledgerId }, cancellationToken: fixture.Token)));
        await fixture.AssertSitesAbsent();
    }

    /// <summary>精确相同编码或活动供应商仓产生插入/启用唯一冲突，返回零后立即回滚，不在失败事务中继续查询。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlMasterDataIntegration")]
    [DataRow("code")]
    [DataRow("supplier")]
    [DataRow("activate")]
    public async Task Management_UniqueConflictReturnsZeroAndRollbackIsSafe(string conflict)
    {
        using var fixture = new Fixture();
        fixture.BeginA();
        try
        {
            var supplier = await fixture.AddSupplier(fixture.A, "S");
            var other = await fixture.AddSupplier(fixture.A, "T");
            var first = fixture.Site("W1", supplier);
            Assert.AreEqual(1, await fixture.RepoA.InsertAsync(first));
            var second = fixture.Site("W2", conflict == "code" ? other : supplier, active: conflict != "activate");
            if (conflict == "code") second.Code = first.Code;
            if (conflict == "activate")
            {
                Assert.AreEqual(1, await fixture.RepoA.InsertAsync(second));
                Assert.AreEqual(0, await fixture.RepoA.ActivateAsync(second, 1));
            }
            else Assert.AreEqual(0, await fixture.RepoA.InsertAsync(second));
        }
        finally { fixture.RollbackA(); }
        await fixture.AssertSitesAbsent();
    }

    /// <summary>确认 B 确被 A 的父仓锁阻塞，A 提交随机仓改名或软删后 B 重新读取当前行，不用超时证明持锁。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlMasterDataIntegration")]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Management_ParentLockWaitRechecksCommittedWarehouse(bool delete)
    {
        using var fixture = new Fixture();
        Task<WarehouseSite?>? pending = null;
        Exception? failure = null;
        try
        {
            fixture.BeginA();
            var supplier = await fixture.AddSupplier(fixture.A, "S");
            var site = fixture.Site("W", supplier);
            Assert.AreEqual(1, await fixture.RepoA.InsertAsync(site));
            fixture.CommitA();
            fixture.BeginA();
            fixture.BeginB();
            var locked = (await fixture.RepoA.GetForUpdateAsync(site.Uid))!;
            if (delete) Assert.AreEqual(1, await fixture.RepoA.DeleteAsync(site.Uid, 1));
            else { locked.Name = "提交后的随机仓名称"; Assert.AreEqual(1, await fixture.RepoA.UpdateNameAsync(locked, 1)); }
            pending = fixture.RepoB.GetForUpdateAsync(site.Uid);
            await fixture.ObserveBlocked(pending);
            fixture.CommitA();
            var current = await pending.WaitAsync(fixture.Token);
            if (delete) Assert.IsNull(current);
            else
            {
                Assert.IsNotNull(current);
                Assert.AreEqual("提交后的随机仓名称", current.Name);
                Assert.AreEqual(2, current.Version);
                Assert.HasCount(0, await fixture.RepoB.GetDeleteBlockersAsync(site.Uid));
            }
            fixture.RollbackB();
        }
        catch (Exception exception) { failure = exception; throw; }
        finally { await fixture.FinishRace(pending, failure); }
    }

    /// <summary>两个真实事务竞争精确同码或同供应商新增/启用；A 成功提交，B 观测等待后返回零并安全回滚。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlMasterDataIntegration")]
    [DataRow("code")]
    [DataRow("supplier")]
    [DataRow("activate")]
    public async Task Management_ConcurrentUniqueWritesHaveOneWinner(string conflict)
    {
        using var fixture = new Fixture();
        Task<int>? pending = null;
        Exception? failure = null;
        try
        {
            fixture.BeginA();
            var supplier = await fixture.AddSupplier(fixture.A, "S");
            var other = await fixture.AddSupplier(fixture.A, "T");
            var first = fixture.Site("W1", supplier, active: conflict != "activate");
            var second = fixture.Site("W2", conflict == "code" ? other : supplier, active: conflict != "activate");
            if (conflict == "code") second.Code = first.Code;
            if (conflict == "activate")
            {
                Assert.AreEqual(1, await fixture.RepoA.InsertAsync(first));
                Assert.AreEqual(1, await fixture.RepoA.InsertAsync(second));
            }
            fixture.CommitA();
            fixture.BeginA();
            fixture.BeginB();
            if (conflict == "activate")
            {
                Assert.IsNotNull(await fixture.RepoA.GetForUpdateAsync(first.Uid));
                Assert.IsNotNull(await fixture.RepoB.GetForUpdateAsync(second.Uid));
                Assert.AreEqual(1, await fixture.RepoA.ActivateAsync(first, 1));
                pending = fixture.RepoB.ActivateAsync(second, 1);
            }
            else
            {
                Assert.AreEqual(1, await fixture.RepoA.InsertAsync(first));
                pending = fixture.RepoB.InsertAsync(second);
            }
            await fixture.ObserveBlocked(pending);
            fixture.CommitA();
            Assert.AreEqual(0, await pending.WaitAsync(fixture.Token));
            fixture.RollbackB();
            var winner = await fixture.ReadSite(fixture.ObserverContext, first.Uid);
            Assert.IsTrue(winner.IsActive);
            if (conflict == "activate")
            {
                Assert.AreEqual(2, winner.Version);
                var loser = await fixture.ReadSite(fixture.ObserverContext, second.Uid);
                Assert.IsFalse(loser.IsActive);
                Assert.AreEqual(1, loser.Version);
            }
            else Assert.AreEqual(0, await fixture.Observer.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT count(*)::int FROM public.warehouse_site WHERE uid=@Uid", new { second.Uid }, cancellationToken: fixture.Token)));
        }
        catch (Exception exception) { failure = exception; throw; }
        finally { await fixture.FinishRace(pending, failure); }
    }

    /// <summary>单片测试局部夹具；唯一提交/清理对象是本例随机供应商和仓库，不操作业务审计、回执或历史流水。</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly CancellationTokenSource deadline = new(TimeSpan.FromSeconds(45));
        private readonly DbSession sessionA;
        private readonly DbSession sessionB;
        private readonly List<Supplier> suppliers = [];
        private readonly List<WarehouseSite> sites = [];
        private Exception? primaryFailure;
        private bool actorsClosed;
        public string Marker { get; } = "WM" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        public string Actor { get; } = "__pgwm_" + Guid.NewGuid().ToString("N");
        public CancellationToken Token => deadline.Token;
        public DbSession Observer { get; }
        public RepositoryBaseContext A { get; }
        public RepositoryBaseContext ObserverContext { get; }
        public PostgreSqlWarehouseManagementRepository RepoA { get; }
        public PostgreSqlWarehouseManagementRepository RepoB { get; }

        /// <summary>固定使用专用保留库，设置驱动命令上限；第三连接只观察阻塞及精确清理本例已提交主数据。</summary>
        public Fixture()
        {
            var settings = new Npgsql.NpgsqlConnectionStringBuilder(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()) { CommandTimeout = 20, Timeout = 10 };
            DbSession? openedA = null, openedB = null, openedObserver = null;
            try
            {
                sessionA = openedA = new DbSession(new PostgreSqlConnectionFactory(settings.ConnectionString));
                sessionB = openedB = new DbSession(new PostgreSqlConnectionFactory(settings.ConnectionString));
                Observer = openedObserver = new DbSession(new PostgreSqlConnectionFactory(settings.ConnectionString));
                var user = new TestUser(Actor);
                A = Context(sessionA, user);
                ObserverContext = Context(Observer, user);
                RepoA = new(A);
                RepoB = new(Context(sessionB, user));
            }
            catch (Exception original)
            {
                var errors = new List<Exception>();
                foreach (var opened in new[] { openedObserver, openedB, openedA })
                    try { opened?.Dispose(); } catch (Exception cleanup) { errors.Add(cleanup); }
                deadline.Dispose();
                if (errors.Count > 0) original.Data["WarehouseManagementConstructionCleanup"] = new AggregateException(errors);
                throw;
            }
        }

        /// <summary>只组装既有上下文，不引入提供者切换或公共测试框架。</summary>
        private static RepositoryBaseContext Context(DbSession session, ICurrentUser user) =>
            new(session, NullLoggerFactory.Instance, new AuditStampService(user), user);

        /// <summary>在 A 开始与父仓重读证明一致的 Read Committed 事务。</summary>
        public void BeginA() => sessionA.Transaction = sessionA.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        /// <summary>在 B 开始独立的竞争事务。</summary>
        public void BeginB() => sessionB.Transaction = sessionB.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        /// <summary>仅并发用例提交随机供应商/仓库；库存、流水、审计及回执用例禁止使用此入口。</summary>
        public void CommitA()
        {
            var transaction = sessionA.Transaction!;
            transaction.Commit();
            transaction.Dispose();
            sessionA.Transaction = null;
        }
        /// <summary>回滚并释放 A 的全部未提交事实。</summary>
        public void RollbackA() => Rollback(sessionA);
        /// <summary>回滚并释放 B，包括唯一冲突后的已失败事务。</summary>
        public void RollbackB() => Rollback(sessionB);

        /// <summary>失败清理不得继续查询已中止事务，统一回滚并清空会话。</summary>
        private static void Rollback(DbSession session)
        {
            if (session.Transaction is not { } transaction) return;
            try { transaction.Rollback(); }
            finally
            {
                try { transaction.Dispose(); }
                finally { session.Transaction = null; }
            }
        }

        /// <summary>建立随机供应商并登记精确清理身份，不借用已有主数据。</summary>
        public async Task<Supplier> AddSupplier(RepositoryBaseContext context, string suffix, bool active = true, bool deleted = false)
        {
            var supplier = new Supplier { Uid = Guid.NewGuid(), SupplierCode = Marker + suffix, SupplierName = "本例供应商" + suffix,
                DocRelateNo = Marker + suffix, IsActive = active, IsDeleted = deleted };
            suppliers.Add(supplier);
            await Insert(context, supplier);
            return supplier;
        }

        /// <summary>登记仓库随机身份和来源快照；仅由调用方选择是否持久化。</summary>
        public WarehouseSite Site(string suffix, Supplier supplier, bool active = true)
        {
            var site = new WarehouseSite { Uid = Guid.NewGuid(), Code = Marker + suffix, Name = "本例仓库" + suffix,
                SupplierId = supplier.Uid, SupplierCodeSnapshot = supplier.SupplierCode, SupplierNameSnapshot = supplier.SupplierName,
                SiteType = WarehouseSiteTypeEnum.SupplierVirtual, IsActive = active };
            sites.Add(site);
            return site;
        }

        /// <summary>按既有 PG 元数据及参数类型插入本例事实；时间、枚举及审计不使用另造映射。</summary>
        public async Task Insert<T>(RepositoryBaseContext context, T entity) where T : BaseEntity
        {
            context.AuditStamp.StampForInsert(entity);
            var metadata = EntityMetadataCache.GetMetadata(typeof(T));
            var columns = string.Join(",", metadata.Columns.Select(column => PostgreSqlNames.Quote(column.ColumnName)));
            var values = string.Join(",", metadata.Columns.Select(column => "@" + column.ParameterName));
            Assert.AreEqual(1, await context.Session.Connection.ExecuteAsync(new CommandDefinition(
                $"INSERT INTO {PostgreSqlNames.Quote(metadata.SchemaName)}.{PostgreSqlNames.Quote(metadata.TableName)} ({columns}) VALUES ({values})",
                PostgreSqlParameters.Entity(entity, metadata), context.Session.Transaction, cancellationToken: Token)));
        }

        /// <summary>读取本例仓库全部字段，允许观察软删版本；不通过默认查询过滤掩盖写入结果。</summary>
        public Task<WarehouseSite> ReadSite(RepositoryBaseContext context, Guid uid)
        {
            var metadata = EntityMetadataCache.GetMetadata(typeof(WarehouseSite));
            var columns = string.Join(",", metadata.Columns.Select(column => $"{PostgreSqlNames.Quote(column.ColumnName)} AS {PostgreSqlNames.Quote(column.PropertyName)}"));
            return context.Session.Connection.QuerySingleAsync<WarehouseSite>(new CommandDefinition(
                $"SELECT {columns} FROM public.warehouse_site WHERE uid=@Uid AND created_by=@Actor", new { Uid = uid, Actor },
                context.Session.Transaction, cancellationToken: Token));
        }

        /// <summary>证明回滚后的本例仓库不存在，不做模糊编码清理或全表统计。</summary>
        public async Task AssertSitesAbsent()
        {
            foreach (var site in sites)
                Assert.AreEqual(0, await Observer.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT count(*)::int FROM public.warehouse_site WHERE uid=@Uid", new { site.Uid }, cancellationToken: Token)));
        }

        /// <summary>仅以服务器报告的精确 A→B 阻塞关系证明竞争；短延时只让出线程，不作为锁证据。</summary>
        public async Task ObserveBlocked(Task pending)
        {
            var pidA = ((Npgsql.NpgsqlConnection)sessionA.Connection).ProcessID;
            var pidB = ((Npgsql.NpgsqlConnection)sessionB.Connection).ProcessID;
            using var observation = CancellationTokenSource.CreateLinkedTokenSource(Token);
            observation.CancelAfter(TimeSpan.FromSeconds(10));
            while (true)
            {
                await Observer.Connection.ExecuteAsync(new CommandDefinition("SELECT pg_stat_clear_snapshot()", cancellationToken: observation.Token));
                var blocked = await Observer.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
                    SELECT EXISTS (SELECT 1 FROM pg_stat_activity a
                        WHERE a.pid=@PidB AND a.state='active' AND a.wait_event_type='Lock'
                          AND @PidA=ANY(pg_blocking_pids(a.pid)))
                    """, new { PidA = pidA, PidB = pidB }, cancellationToken: observation.Token));
                if (blocked) return;
                Assert.IsFalse(pending.IsCompleted, "未观测到指定父行/唯一索引竞争，操作已经结束。");
                await Task.Delay(40, observation.Token);
            }
        }

        /// <summary>先由调用方释放 A 的锁，再有界等待竞争任务结束；失败时关闭 B 连接并观察其异常。</summary>
        public async Task Drain(Task? pending)
        {
            if (pending is null) return;
            try { await pending.WaitAsync(TimeSpan.FromSeconds(22)); }
            catch
            {
                sessionB.Connection.Close();
                try { await pending.WaitAsync(TimeSpan.FromSeconds(2)); } catch { /* 任务失败由原断言报告，防止未观察异常。 */ }
                throw;
            }
        }

        /// <summary>只按 UID、随机编码和唯一审计人联合删除本例可变主数据；仓库先于供应商，绝不清理余额或流水。</summary>
        public async Task CleanupCommittedMasterData()
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            foreach (var site in sites)
                await Observer.Connection.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM public.warehouse_site WHERE uid=@Uid AND code=@Code AND created_by=@Actor",
                    new { site.Uid, site.Code, Actor }, cancellationToken: cleanup.Token));
            foreach (var supplier in suppliers)
                await Observer.Connection.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM public.supplier WHERE uid=@Uid AND supplier_code=@Code AND created_by=@Actor",
                    new { supplier.Uid, Code = supplier.SupplierCode, Actor }, cancellationToken: cleanup.Token));
            foreach (var site in sites)
                Assert.AreEqual(0, await Observer.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT count(*)::int FROM public.warehouse_site WHERE uid=@Uid", new { site.Uid }, cancellationToken: cleanup.Token)));
            foreach (var supplier in suppliers)
                Assert.AreEqual(0, await Observer.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT count(*)::int FROM public.supplier WHERE uid=@Uid", new { supplier.Uid }, cancellationToken: cleanup.Token)));
        }

        /// <summary>不论哪个失败清理步骤抛错都继续释放两会话并精确清理；原断言异常保持原栈，清理故障附加到异常数据。</summary>
        public async Task FinishRace(Task? pending, Exception? original)
        {
            primaryFailure = original;
            var errors = new List<Exception>();
            try { RollbackA(); } catch (Exception error) { errors.Add(error); }
            try { await Drain(pending); } catch (Exception error) { errors.Add(error); }
            try { RollbackB(); } catch (Exception error) { errors.Add(error); }
            try { CloseActors(); } catch (Exception error) { errors.Add(error); }
            try { await CleanupCommittedMasterData(); } catch (Exception error) { errors.Add(error); }
            if (errors.Count == 0) return;
            var cleanup = new AggregateException("仓库管理随机夹具清理未全部完成。", errors);
            if (original is not null)
            {
                original.Data["WarehouseManagementCleanup"] = cleanup;
                Console.Error.WriteLine("仓库管理夹具清理也发生错误；已保留原测试异常，并在异常 Data 中附加清理故障。");
                return;
            }
            primaryFailure = cleanup;
            throw cleanup;
        }

        /// <summary>两个竞争会话释放后才允许观察连接执行精确清理，避免残留事务继续持锁。</summary>
        public void CloseActors()
        {
            var errors = new List<Exception>();
            try { sessionA.Dispose(); } catch (Exception error) { errors.Add(error); }
            try { sessionB.Dispose(); } catch (Exception error) { errors.Add(error); }
            actorsClosed = true;
            if (errors.Count > 0) throw new AggregateException("竞争会话释放失败。", errors);
        }

        /// <summary>释放连接前回滚残留事务；不会把清理升级为提交。</summary>
        public void Dispose()
        {
            var errors = new List<Exception>();
            if (!actorsClosed)
            {
                try { RollbackA(); } catch (Exception error) { errors.Add(error); }
                try { RollbackB(); } catch (Exception error) { errors.Add(error); }
                try { CloseActors(); } catch (Exception error) { errors.Add(error); }
            }
            try { Observer.Dispose(); } catch (Exception error) { errors.Add(error); }
            deadline.Dispose();
            if (errors.Count == 0) return;
            var cleanup = new AggregateException("仓库管理夹具释放失败。", errors);
            if (primaryFailure is not null) primaryFailure.Data["WarehouseManagementDispose"] = cleanup;
            else throw cleanup;
        }
    }

    /// <summary>每例唯一的审计身份使主数据清理具备第三重精确限制。</summary>
    private sealed class TestUser(string name) : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => name;
        public bool IsAuthenticated => false;
    }
}
