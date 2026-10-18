using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Customers.PoConfigs;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>仅在获准初始化并迁移的新四表专库运行；不建库、不迁移、不使用 Legacy 夹具。</summary>
[TestClass]
[TestCategory("CustomerPoConfigIdentityIntegration")]
[DoNotParallelize]
public sealed partial class CustomerPoConfigIdentityPostgreSqlTests
{
    private static readonly Guid Customer = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Supplier = Guid.Parse("22222222-2222-4222-8222-222222222222");

    /// <summary>当前编码跟随主数据、双方精确分页和 ID 唯一约束、数字边界与乐观版本均使用真实 PG，最终整例回滚。</summary>
    [TestMethod]
    public async Task IdentityReadUniqueAndVersion_RollBackWholeCase()
    {
        using var session = Session();
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        var repository = new PostgreSqlCustomerPoConfigRepository(Context(session));
        try
        {
            var seed = await repository.GetByPartiesAsync(Customer, Supplier);
            Assert.IsNotNull(seed, "必须先人工完成本轮夹具初始化和身份迁移。");
            await session.Connection.ExecuteAsync("UPDATE public.customer SET customer_code='C-CONFIG-RENAMED' WHERE uid=@Customer", new { Customer }, transaction);
            var current = await repository.GetByPartiesAsync(Customer, Supplier);
            Assert.AreEqual("C-CONFIG-RENAMED", current!.CustomerCode);
            var page = await repository.GetPagedAsync(new() { CustomerId = Customer, SupplierId = Supplier, Keyword = "RENAMED" });
            Assert.AreEqual(1, page.TotalCount); Assert.AreEqual(seed.Uid, page.Items.Single().Uid);
            await session.Connection.ExecuteAsync("SAVEPOINT identity_probe", transaction: transaction);
            var collision = await Assert.ThrowsExactlyAsync<PostgresException>(() => repository.AddAsync(new()
            { CustomerId = Customer, SupplierId = Supplier, CustomerCode = "DIFFERENT", SupplierCode = "DIFFERENT" }));
            Assert.AreEqual(PostgresErrorCodes.UniqueViolation, collision.SqlState);
            Assert.AreEqual("ux_customer_po_config_parties", collision.ConstraintName);
            await session.Connection.ExecuteAsync("ROLLBACK TO SAVEPOINT identity_probe", transaction: transaction);
            var row = await repository.GetByIdAsync(seed.Uid);
            Assert.IsNotNull(row); var oldVersion = row.Version;
            // 通用仓储会把新版本回写到入参；冲突必须使用写入前独立读取的旧快照。
            var stale = await repository.GetByIdAsync(seed.Uid);
            Assert.IsNotNull(stale);
            row.DefaultDiscountRate = 999.999999m;
            Assert.AreEqual(oldVersion + 1, (await repository.UpdateAsync(row)).NewVersion);
            Assert.IsFalse((await repository.UpdateAsync(stale)).Success, "旧版本不能覆盖上次写入。");
            foreach (var allowed in new decimal?[] { null, 0m, 1.05m, 999.999999m })
            {
                await session.Connection.ExecuteAsync("UPDATE public.customer_po_config SET default_discount_rate=@Rate WHERE uid=@Uid", new { Rate = allowed, seed.Uid }, transaction);
                Assert.AreEqual(allowed, await session.Connection.QuerySingleAsync<decimal?>("SELECT default_discount_rate FROM public.customer_po_config WHERE uid=@Uid", new { seed.Uid }, transaction));
            }
            await session.Connection.ExecuteAsync("SAVEPOINT negative_rate_probe", transaction: transaction);
            var negative = await Assert.ThrowsExactlyAsync<PostgresException>(() => session.Connection.ExecuteAsync(
                "UPDATE public.customer_po_config SET default_discount_rate=-0.000001 WHERE uid=@Uid", new { seed.Uid }, transaction));
            Assert.AreEqual(PostgresErrorCodes.CheckViolation, negative.SqlState);
            await session.Connection.ExecuteAsync("ROLLBACK TO SAVEPOINT negative_rate_probe", transaction: transaction);
            await session.Connection.ExecuteAsync("SAVEPOINT rate_probe", transaction: transaction);
            var rateError = await Assert.ThrowsExactlyAsync<PostgresException>(() => session.Connection.ExecuteAsync(
                "UPDATE public.customer_po_config SET default_discount_rate=1000 WHERE uid=@Uid", new { seed.Uid }, transaction));
            Assert.AreEqual(PostgresErrorCodes.NumericValueOutOfRange, rateError.SqlState);
        }
        finally { transaction.Rollback(); session.Transaction = null; }
    }

    /// <summary>两个连接争用本例新建供应商的同一 ID 配置，真实观察唯一键等待，最后仅清理本例精确身份。</summary>
    [TestMethod]
    public async Task IdentityUnique_TwoConnectionsHaveOneWinner()
    {
        using var owner = Session(); using var waiter = Session();
        using var ownerTx = owner.Connection.BeginTransaction(IsolationLevel.ReadCommitted); owner.Transaction = ownerTx;
        using var waiterTx = waiter.Connection.BeginTransaction(IsolationLevel.ReadCommitted); waiter.Transaction = waiterTx;
        var supplierId = Guid.NewGuid(); var marker = "CFG" + Guid.NewGuid().ToString("N");
        var first = new CustomerPoConfig { CustomerId = Customer, SupplierId = supplierId, CustomerCode = "C-CONFIG", SupplierCode = marker };
        var second = new CustomerPoConfig { CustomerId = Customer, SupplierId = supplierId, CustomerCode = "STALE-CODE", SupplierCode = "STALE-CODE" };
        Task<SwiftExport.Contracts.Wrappers.RepositoryWriteResult>? pending = null;
        var committed = false;
        try
        {
            await owner.Connection.ExecuteAsync("INSERT INTO public.supplier(uid,is_active,supplier_code,supplier_name,doc_relate_no) VALUES(@supplierId,FALSE,@marker,@marker,@marker)", new { supplierId, marker }, ownerTx);
            await new PostgreSqlCustomerPoConfigRepository(Context(owner)).AddAsync(first);
            await waiter.Connection.ExecuteAsync("SET LOCAL statement_timeout='15s'", transaction: waiterTx);
            var pid = await waiter.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()", transaction: waiterTx);
            pending = new PostgreSqlCustomerPoConfigRepository(Context(waiter)).AddAsync(second);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!await owner.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT pg_backend_pid()=ANY(pg_blocking_pids(@pid))", new { pid }, ownerTx, cancellationToken: timeout.Token)))
            {
                Assert.IsFalse(pending.IsCompleted, "竞争插入应等待持有者。");
                await Task.Delay(25, timeout.Token);
            }
            ownerTx.Commit(); committed = true; owner.Transaction = null;
            var error = await Assert.ThrowsExactlyAsync<PostgresException>(async () => await pending);
            Assert.AreEqual(PostgresErrorCodes.UniqueViolation, error.SqlState);
            Assert.AreEqual("ux_customer_po_config_parties", error.ConstraintName);
        }
        finally
        {
            if (!committed) ownerTx.Rollback(); owner.Transaction = null;
            try { if (pending is not null) { try { await pending; } catch (PostgresException) { } } }
            finally
            {
                waiterTx.Rollback(); waiter.Transaction = null;
                await owner.Connection.ExecuteAsync("DELETE FROM public.customer_po_config WHERE uid=ANY(@Ids)", new { Ids = new[] { first.Uid, second.Uid } });
                await owner.Connection.ExecuteAsync("DELETE FROM public.supplier WHERE uid=@supplierId", new { supplierId });
            }
        }
    }

    /// <summary>没有专用显式连接则不运行；即使提供连接也必须命中唯一获准新库及固定实例，不能落到运行库。</summary>
    private static DbSession Session()
    {
        var value = Environment.GetEnvironmentVariable("SWIFTEXPORT_PO_CONFIG_IDENTITY_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(value) && Environment.GetEnvironmentVariable("SWIFTEXPORT_PO_CONFIG_IDENTITY_APPROVED") == "1")
            value = ApprovedConnection("dv_customer_po_config_tests_20261018").ConnectionString;
        if (string.IsNullOrWhiteSpace(value)) Assert.Inconclusive("未提供本轮专库连接，实库测试未执行。");
        var builder = new NpgsqlConnectionStringBuilder(value!);
        if (builder.Host != "192.168.3.12" || builder.Port != 5432 || builder.Database != "dv_customer_po_config_tests_20261018")
            throw new InvalidOperationException("客户采购配置实库测试目标不在本轮白名单。");
        return new(new PostgreSqlConnectionFactory(value!));
    }
    /// <summary>所有实际仓储共享真实会话及测试审计，不覆盖用户凭据。</summary>
    private static RepositoryBaseContext Context(DbSession session)
    { var user = new TestUser(); return new(session, NullLoggerFactory.Instance, new AuditStampService(user), user); }
    /// <summary>仅用于本轮隔离夹具审计。</summary>
    private sealed class TestUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "CustomerPoConfigIdentityTest";
        public bool IsAuthenticated => false;
    }
}
