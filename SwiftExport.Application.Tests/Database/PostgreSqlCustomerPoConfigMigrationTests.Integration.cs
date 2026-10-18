using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Customers.PoConfigs;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>仅供主控在固定专用库串行验收 PO 仓储；普通场景回滚，竞争场景只清理本例精确 Uid，不初始化结构。</summary>
[TestClass]
[TestCategory("PostgreSqlCustomerPoConfigIntegration")]
[DoNotParallelize]
public sealed class PostgreSqlCustomerPoConfigMigrationIntegrationTests
{
    /// <summary>双方可停用、编码仅缺 UID 时参与解析；失败不回填、已删除身份不可用，空 UID 不当缺失。</summary>
    [TestMethod]
    public Task Relations_PreserveUidPriorityBackfillAndDeletedInactiveBoundaries() => InRollback(async (context, repository) =>
    {
        var customer = new Customer { CustomerCode = Prefix(), CustomerName = "临时客户", DocRelateNo = Prefix(), IsActive = false };
        var supplier = new Supplier { SupplierCode = Prefix(), SupplierName = "临时供应商", DocRelateNo = Prefix(), Currency = "CNY", IsActive = false };
        await Add(context, customer); await Add(context, supplier);
        var entity = Config(customer.Uid, supplier.Uid);
        entity.CustomerCode = "错误旧码"; entity.SupplierCode = "错误旧码";
        Assert.IsTrue(await repository.PopulateRelationIdsAsync(entity));
        Assert.AreEqual(customer.CustomerCode, entity.CustomerCode); Assert.AreEqual(supplier.SupplierCode, entity.SupplierCode);
        entity.CustomerId = null; entity.SupplierId = null;
        entity.CustomerCode = customer.CustomerCode.ToLowerInvariant() + " "; entity.SupplierCode = supplier.SupplierCode.ToLowerInvariant();
        Assert.IsTrue(await repository.PopulateRelationIdsAsync(entity));
        Assert.AreEqual(customer.Uid, entity.CustomerId); Assert.AreEqual(supplier.Uid, entity.SupplierId);
        var unresolved = new CustomerPoConfig { CustomerId = Guid.Empty, CustomerCode = customer.CustomerCode, SupplierCode = supplier.SupplierCode };
        Assert.IsFalse(await repository.PopulateRelationIdsAsync(unresolved));
        Assert.AreEqual(Guid.Empty, unresolved.CustomerId); Assert.IsNull(unresolved.SupplierId);
        Assert.IsTrue((await new PostgreSqlDapperRepository<Supplier>(context).DeleteAsync(supplier.Uid, 1)).Success);
        var before = entity.SupplierCode;
        Assert.IsFalse(await repository.PopulateRelationIdsAsync(entity));
        Assert.AreEqual(before, entity.SupplierCode); Assert.AreEqual(customer.Uid, entity.CustomerId);
    });

    /// <summary>验证完整 DTO、继承九方法、审计、折扣精度、软硬删除及版本失败，事务仍由测试拥有。</summary>
    [TestMethod]
    public Task Crud_ProjectionVersionDeletionAndInheritedQueries() => InRollback(async (context, repository) =>
    {
        var row = Config(Guid.NewGuid(), Guid.NewGuid()); row.Currency = null; row.DefaultDiscountRate = 1.05m; row.DiscountRemark = "独立折扣备注"; row.IMType = "说明书";
        Assert.IsTrue((await repository.AddAsync(row)).Success);
        Assert.IsTrue(await repository.ExistsIdAsync(row.Uid));
        CollectionAssert.Contains((await repository.GetExistingIdListAsync([row.Uid])).ToArray(), row.Uid);
        Assert.IsNotNull(await repository.GetSingleEntityAsync());
        Assert.IsTrue((await repository.GetAllAsync()).Any(item => item.Uid == row.Uid));
        Assert.HasCount(1, await repository.GetByIdListAsync([row.Uid]));
        var dto = await repository.GetByPartiesAsync(row.CustomerId!.Value, row.SupplierId!.Value, withUpdLock: true);
        Assert.IsNotNull(dto); Assert.IsNull(dto.Currency); Assert.AreEqual(1.05m, dto.DefaultDiscountRate); Assert.AreEqual("说明书", dto.IMType);
        Assert.AreEqual("独立折扣备注", dto.DiscountRemark); Assert.IsNotNull(dto.CreatedDate);
        var current = await repository.GetByIdAsync(row.Uid); var stale = await repository.GetByIdAsync(row.Uid);
        Assert.IsNotNull(current); Assert.IsNotNull(stale); current.Note = "修改";
        Assert.AreEqual(2, (await repository.UpdateAsync(current)).NewVersion);
        Assert.AreEqual("CONCURRENCY_CONFLICT", (await repository.UpdateAsync(stale)).ErrorCode);
        Assert.AreEqual("CONCURRENCY_CONFLICT", (await repository.DeleteAsync(row.Uid, 1)).ErrorCode);
        Assert.IsTrue((await repository.DeleteAsync(row.Uid, 2)).Success);
        Assert.IsNull(await repository.GetByPartiesAsync(row.CustomerId.Value, row.SupplierId.Value));
        Assert.IsNull(await repository.GetByIdAsync(row.Uid));
        Assert.IsTrue(await repository.ExistsIdAsync(row.Uid));
        var hard = Config(Guid.NewGuid(), Guid.NewGuid()); await repository.AddAsync(hard);
        Assert.IsTrue((await repository.DeleteAsync(hard.Uid, 1, false)).Success);
        Assert.IsFalse(await repository.ExistsIdAsync(hard.Uid)); Assert.IsNotNull(context.Session.Transaction);
    });

    /// <summary>三检查保持原内存比较差异和已删范围，关键词包含百分号仍按字面匹配，分页计数不扩大交叉组合。</summary>
    [TestMethod]
    public Task PairChecksAndPaging_KeepTrimCaseCrossPairsAndDeletedRows() => InRollback(async (context, repository) =>
    {
        var prefix = Prefix();
        var first = Config(Guid.NewGuid(), Guid.NewGuid()); first.CustomerCode = prefix + "A"; first.SupplierCode = prefix + "S ";
        var second = Config(Guid.NewGuid(), Guid.NewGuid()); second.CustomerCode = prefix + "B"; second.SupplierCode = prefix + "T";
        var cross = Config(Guid.NewGuid(), Guid.NewGuid()); cross.CustomerCode = first.CustomerCode; cross.SupplierCode = second.SupplierCode;
        foreach (var row in new[] { first, second, cross }) Assert.IsTrue((await repository.AddAsync(row)).Success);
        var keys = new[] { Check(first), Check(second) };
        Assert.HasCount(2, await repository.GetExistingCustomerCodesAsync(keys));
        Assert.HasCount(0, await repository.GetUsedByCustomerCodeAndSupplierCodeAsync([Check(first)]), "原占用内存比较不修剪数据库尾空格。");
        var lower = new PoConfigCheck { CustomerCode = second.CustomerCode.ToLowerInvariant(), SupplierCode = second.SupplierCode.ToLowerInvariant() };
        Assert.HasCount(1, await repository.GetExistingCustomerCodesAsync([lower]));
        Assert.HasCount(0, await repository.GetNonUniqueByPoConfigCheckList([lower]));
        Assert.HasCount(0, await repository.GetUsedByCustomerCodeAndSupplierCodeAsync([Check(second)]));
        lower.Uid = first.Uid;
        Assert.HasCount(1, await repository.GetUsedByCustomerCodeAndSupplierCodeAsync([lower]));
        Assert.IsTrue((await repository.DeleteAsync(second.Uid, 1)).Success);
        Assert.HasCount(0, await repository.GetExistingCustomerCodesAsync([Check(second)]));
        Assert.HasCount(1, await repository.GetNonUniqueByPoConfigCheckList([Check(second)]));
        var literal = Config(Guid.NewGuid(), Guid.NewGuid()); literal.CustomerCode = prefix + "%"; await repository.AddAsync(literal);
        var page = await repository.GetPagedAsync(new CustomerPoConfigSearchRequest { Keyword = prefix + "%", PageIndex = 0, PageSize = 999 });
        Assert.AreEqual(1, page.TotalCount); Assert.AreEqual(literal.Uid, page.Items.Single().Uid); Assert.AreEqual(1, page.PageIndex); Assert.AreEqual(500, page.PageSize);
    });

    /// <summary>真实唯一索引按双方身份拒绝插入和更新碰撞；编码重复或为空不再替代身份。</summary>
    [TestMethod]
    public Task UniqueIndex_RejectsIdentityCollisionsRegardlessOfCodes() => InRollback(async (context, repository) =>
    {
        var first = Config(Guid.NewGuid(), Guid.NewGuid()); await repository.AddAsync(first);
        var duplicate = Config(first.CustomerId!.Value, first.SupplierId!.Value);
        duplicate.CustomerCode = Prefix(); duplicate.SupplierCode = Prefix();
        await Collision(context, () => repository.AddAsync(duplicate));
        var changed = Config(Guid.NewGuid(), Guid.NewGuid()); await repository.AddAsync(changed);
        changed.CustomerId = first.CustomerId; changed.SupplierId = first.SupplierId;
        await Collision(context, () => repository.UpdateAsync(changed));
        var independent = Config(Guid.NewGuid(), Guid.NewGuid());
        independent.CustomerCode = first.CustomerCode; independent.SupplierCode = first.SupplierCode;
        Assert.IsTrue((await repository.AddAsync(independent)).Success);
        var nullableText = Config(Guid.NewGuid(), Guid.NewGuid()); nullableText.CustomerCode = null;
        Assert.IsTrue((await repository.AddAsync(nullableText)).Success);
        Assert.IsTrue(await repository.ExistsIdAsync(first.Uid));
    });
    /// <summary>在本例回滚事务内创建随机停用币种，验证 PO 保留其引用；不依赖或修改既有币种，不替代服务完整校验。</summary>
    [TestMethod]
    public Task DisabledCurrencyReference_IsPreserved() => InRollback(async (context, repository) =>
    {
        // PO 币种列限长 10，随机代码取 10 字符；非本位、非系统币种及正汇率不改变全库本位币事实。
        var currency = new SwiftExport.Core.Entities.Systems.Currencies.SysDicCurrency
        {
            CurrencyCode = "P" + Guid.NewGuid().ToString("N")[..9],
            CurrencyNameEn = "PO disabled currency fixture",
            Precision = 2, RateToBase = 1m, IsBase = false, IsSystem = false, IsEnabled = false,
            Remark = "__pgtest_po_config_disabled_currency__"
        };
        await Add(context, currency);
        var savedCurrency = await new PostgreSqlDapperRepository<SwiftExport.Core.Entities.Systems.Currencies.SysDicCurrency>(context).GetByIdAsync(currency.Uid);
        Assert.IsNotNull(savedCurrency); Assert.IsFalse(savedCurrency.IsEnabled); Assert.IsFalse(savedCurrency.IsBase);
        var row = Config(Guid.NewGuid(), Guid.NewGuid()); row.Currency = currency.CurrencyCode;
        Assert.IsTrue((await repository.AddAsync(row)).Success);
        var dto = await repository.GetByPartiesAsync(row.CustomerId!.Value, row.SupplierId!.Value);
        Assert.IsNotNull(dto); Assert.AreEqual(currency.CurrencyCode, dto.Currency);
    });

    /// <summary>缺失配置锁定阻塞其他连接 INSERT，提交和回滚均释放；普通预览不阻塞，双连接写入均回滚。</summary>
    [TestMethod]
    [DataRow(true, true)]
    [DataRow(true, false)]
    [DataRow(false, false)]
    public async Task TableLock_MissingResultBlocksInsertOnlyForLockedRead(bool locked, bool commit)
    {
        using var owner = Session(); using var waiter = Session();
        using var ownerTx = owner.Connection.BeginTransaction(IsolationLevel.ReadCommitted); owner.Transaction = ownerTx;
        using var waiterTx = waiter.Connection.BeginTransaction(IsolationLevel.ReadCommitted); waiter.Transaction = waiterTx;
        var ownerRepo = new PostgreSqlCustomerPoConfigRepository(Context(owner)); var waiterRepo = new PostgreSqlCustomerPoConfigRepository(Context(waiter));
        var row = Config(Guid.NewGuid(), Guid.NewGuid());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        // 泛型新增无取消参数，事务内 statement_timeout 兜底，不能遗留无限等待的连接。
        await waiter.Connection.ExecuteAsync("SET LOCAL statement_timeout='15s'", transaction: waiterTx);
        var waiterPid = await waiter.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()", transaction: waiterTx);
        Task<SwiftExport.Contracts.Wrappers.RepositoryWriteResult>? pending = null;
        var released = false;
        try
        {
            Assert.IsNull(await ownerRepo.GetByPartiesAsync(row.CustomerId!.Value, row.SupplierId!.Value, timeout.Token, locked));
            pending = waiterRepo.AddAsync(row);
            if (locked) await AssertBlocked(owner, waiterPid, pending, timeout.Token);
            else Assert.IsTrue((await pending.WaitAsync(timeout.Token)).Success);
            if (commit) ownerTx.Commit(); else ownerTx.Rollback(); released = true; owner.Transaction = null;
            Assert.IsTrue((await pending.WaitAsync(timeout.Token)).Success);
        }
        finally
        {
            if (!released) ownerTx.Rollback(); owner.Transaction = null;
            try { if (pending is not null) await pending; }
            finally { waiterTx.Rollback(); waiter.Transaction = null; }
        }
    }

    /// <summary>先写后锁的另一连接等待写事务结束，后续查询应看到该事务已提交的新配置；仅清理本例已提交 UID。</summary>
    [TestMethod]
    public async Task LockedRead_AfterWaitingSeesNewlyCommittedConfiguration()
    {
        using var writer = Session(); using var reader = Session();
        using var writeTx = writer.Connection.BeginTransaction(IsolationLevel.ReadCommitted); writer.Transaction = writeTx;
        using var readTx = reader.Connection.BeginTransaction(IsolationLevel.ReadCommitted); reader.Transaction = readTx;
        var row = Config(Guid.NewGuid(), Guid.NewGuid()); var committed = false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var readerPid = await reader.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()", transaction: readTx);
        Task<SwiftExport.Contracts.DTOs.Customers.CustomerPoConfigDto?>? pending = null;
        try
        {
            await new PostgreSqlCustomerPoConfigRepository(Context(writer)).AddAsync(row);
            pending = new PostgreSqlCustomerPoConfigRepository(Context(reader)).GetByPartiesAsync(row.CustomerId!.Value, row.SupplierId!.Value, timeout.Token, true);
            await AssertBlocked(writer, readerPid, pending, timeout.Token);
            writeTx.Commit(); committed = true; writer.Transaction = null;
            var dto = await pending; Assert.IsNotNull(dto); Assert.AreEqual(row.Uid, dto.Uid);
        }
        finally
        {
            if (!committed) writeTx.Rollback(); writer.Transaction = null;
            try { if (pending is not null) { try { await pending; } catch (OperationCanceledException) { } } }
            finally { readTx.Rollback(); reader.Transaction = null; await Cleanup(writer, row.Uid); }
        }
    }

    /// <summary>已存在配置的锁定读取阻塞其他连接更新或删除，释放后完成，随机提交夹具按精确 UID 清理。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TableLock_ExistingConfigurationBlocksUpdateOrDelete(bool delete)
    {
        using var owner = Session(); using var waiter = Session();
        var row = Config(Guid.NewGuid(), Guid.NewGuid()); await new PostgreSqlCustomerPoConfigRepository(Context(owner)).AddAsync(row);
        using var ownerTx = owner.Connection.BeginTransaction(IsolationLevel.ReadCommitted); owner.Transaction = ownerTx;
        using var waiterTx = waiter.Connection.BeginTransaction(IsolationLevel.ReadCommitted); waiter.Transaction = waiterTx;
        await waiter.Connection.ExecuteAsync("SET LOCAL statement_timeout='15s'", transaction: waiterTx);
        var pid = await waiter.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()", transaction: waiterTx);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Task<int>? pending = null; var released = false;
        try
        {
            Assert.IsNotNull(await new PostgreSqlCustomerPoConfigRepository(Context(owner)).GetByPartiesAsync(row.CustomerId!.Value, row.SupplierId!.Value, timeout.Token, true));
            pending = waiter.Connection.ExecuteAsync(delete ? "DELETE FROM public.customer_po_config WHERE uid=@Uid" : "UPDATE public.customer_po_config SET note='竞争更新' WHERE uid=@Uid", new { row.Uid }, waiterTx);
            await AssertBlocked(owner, pid, pending, timeout.Token);
            ownerTx.Rollback(); released = true; owner.Transaction = null;
            Assert.AreEqual(1, await pending.WaitAsync(timeout.Token));
        }
        finally
        {
            if (!released) ownerTx.Rollback(); owner.Transaction = null;
            try { if (pending is not null) await pending; }
            finally { waiterTx.Rollback(); waiter.Transaction = null; await Cleanup(owner, row.Uid); }
        }
    }

    /// <summary>双连接唯一竞争等待胜者提交后产生准确 PO 索引错误；败者回滚、胜者随机 UID 精确清理。</summary>
    [TestMethod]
    public async Task UniqueCompetition_WaitsThenRejectsCommittedWinner()
    {
        using var owner = Session(); using var waiter = Session();
        using var ownerTx = owner.Connection.BeginTransaction(IsolationLevel.ReadCommitted); owner.Transaction = ownerTx;
        using var waiterTx = waiter.Connection.BeginTransaction(IsolationLevel.ReadCommitted); waiter.Transaction = waiterTx;
        await waiter.Connection.ExecuteAsync("SET LOCAL statement_timeout='15s'", transaction: waiterTx);
        var pid = await waiter.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()", transaction: waiterTx);
        var row = Config(Guid.NewGuid(), Guid.NewGuid()); var duplicate = Config(row.CustomerId!.Value, row.SupplierId!.Value);
        duplicate.CustomerCode = row.CustomerCode!.ToLowerInvariant() + " "; duplicate.SupplierCode = row.SupplierCode!.ToLowerInvariant();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Task<SwiftExport.Contracts.Wrappers.RepositoryWriteResult>? pending = null; var committed = false;
        try
        {
            await new PostgreSqlCustomerPoConfigRepository(Context(owner)).AddAsync(row);
            pending = new PostgreSqlCustomerPoConfigRepository(Context(waiter)).AddAsync(duplicate);
            await AssertBlocked(owner, pid, pending, timeout.Token);
            ownerTx.Commit(); committed = true; owner.Transaction = null;
            var error = await Assert.ThrowsAsync<PostgresException>(async () => await pending.WaitAsync(timeout.Token));
            Assert.AreEqual("23505", error.SqlState); Assert.AreEqual("ux_customer_po_config_parties", error.ConstraintName);
        }
        finally
        {
            if (!committed) ownerTx.Rollback(); owner.Transaction = null;
            try { if (pending is not null) { try { await pending; } catch (PostgresException) { } } }
            finally { waiterTx.Rollback(); waiter.Transaction = null; await Cleanup(owner, row.Uid); }
        }
    }

    /// <summary>固定专用库会话，复用现有目标校验，不提供默认连接或修改环境变量。</summary>
    private static DbSession Session() => new(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));

    /// <summary>共享单会话事务和真实审计组件。</summary>
    private static RepositoryBaseContext Context(DbSession session)
    { var user = new PoUser(); return new(session, NullLoggerFactory.Instance, new AuditStampService(user), user); }

    /// <summary>普通场景无条件回滚随机实体，不触碰结构或旧行。</summary>
    private static async Task InRollback(Func<RepositoryBaseContext, PostgreSqlCustomerPoConfigRepository, Task> action)
    {
        using var session = Session(); using var tx = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted); session.Transaction = tx;
        var context = Context(session);
        try { await action(context, new PostgreSqlCustomerPoConfigRepository(context)); }
        finally { try { tx.Rollback(); } finally { session.Transaction = null; } }
    }

    /// <summary>在保存点内检验提供者唯一异常，不让预期错误破坏外层测试事务。</summary>
    private static async Task Collision(RepositoryBaseContext context, Func<Task<SwiftExport.Contracts.Wrappers.RepositoryWriteResult>> action)
    {
        await context.Session.Connection.ExecuteAsync("SAVEPOINT po_collision", transaction: context.Session.Transaction);
        try { var error = await Assert.ThrowsAsync<PostgresException>(async () => await action()); Assert.AreEqual("23505", error.SqlState); Assert.AreEqual("ux_customer_po_config_parties", error.ConstraintName); }
        finally { await context.Session.Connection.ExecuteAsync("ROLLBACK TO SAVEPOINT po_collision; RELEASE SAVEPOINT po_collision", transaction: context.Session.Transaction); }
    }

    /// <summary>轮询真实阻塞关系，而非以睡眠时长猜测锁；限时内失败仍由外层释放事务。</summary>
    private static async Task AssertBlocked(DbSession owner, int waiterPid, Task pending, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (await owner.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT pg_backend_pid()=ANY(pg_blocking_pids(@Pid))", new { Pid = waiterPid }, owner.Transaction, cancellationToken: token)))
            { Assert.IsFalse(pending.IsCompleted); return; }
            Assert.IsFalse(pending.IsCompleted, "命令没有等待预期持有者。");
            await Task.Delay(25, token);
        }
        Assert.Fail("限时内未观察到真实阻塞关系。");
    }

    /// <summary>仅移除本例记录的确切随机 UID，并确认无残留；不按前缀清理、不枚举旧数据。</summary>
    private static async Task Cleanup(DbSession session, Guid uid)
    {
        if (uid == Guid.Empty) return;
        await session.Connection.ExecuteAsync("DELETE FROM public.customer_po_config WHERE uid=@Uid", new { Uid = uid });
        Assert.AreEqual(0, await session.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.customer_po_config WHERE uid=@Uid", new { Uid = uid }));
    }

    /// <summary>通用仓储插入只属于本例的主数据实体。</summary>
    private static async Task Add<T>(RepositoryBaseContext context, T row) where T : BaseEntity
    { Assert.IsTrue((await new PostgreSqlDapperRepository<T>(context).AddAsync(row)).Success); }

    /// <summary>每次生成独立短业务编码，确保测试间无键碰撞。</summary>
    private static string Prefix() => "PGPO" + Guid.NewGuid().ToString("N");

    /// <summary>构造无外键约束的随机 PO 配置，不创建无关业务对象。</summary>
    private static CustomerPoConfig Config(Guid customer, Guid supplier) => new()
    { CustomerId = customer, SupplierId = supplier, CustomerCode = Prefix(), SupplierCode = Prefix(), Note = "__pgtest_po_config__" };

    /// <summary>按已存储原文构造组合检查，不预先 Trim。</summary>
    private static PoConfigCheck Check(CustomerPoConfig row) => new() { Uid = row.Uid, CustomerCode = row.CustomerCode, SupplierCode = row.SupplierCode };

    /// <summary>固定测试审计名，不借用生产身份。</summary>
    private sealed class PoUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "__pgtest_po_config__";
        public bool IsAuthenticated => false;
    }
}
