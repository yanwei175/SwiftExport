using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Commands;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Sources.Document;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>真实物理连接验证全局命令项与来源准备的顺序、绑定重验和释放；不冒充完整Net/Link仓储或采购服务验收。</summary>
[TestClass, DoNotParallelize, TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlMaterialPreparationIntegrationTests
{
    /// <summary>相同全局命令项跨来源互斥，不同命令项及来源可独立准备；等待者尚未持有自己的S/U或建立父快照。</summary>
    [TestMethod]
    public async Task GlobalItemSerializesAcrossSources_IndependentItemCanProceed()
    {
        await using var holder = new Fixture(); await using var waiter = new Fixture();
        await using var independent = new Fixture(); await using var observer = new Fixture();
        Assert.AreNotEqual(holder.Pid, waiter.Pid); Assert.AreNotEqual(waiter.Pid, observer.Pid);
        var item = Guid.NewGuid(); var source = Guid.NewGuid(); var gross = Guid.NewGuid();
        var held = await holder.Repository.AcquireMaterialBeforeTransactionAsync([Guid.NewGuid()], [Guid.NewGuid()], [], [item, item]);
        Assert.IsNotNull(held); Task<IAsyncDisposable?>? pending = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await using var other = await independent.Repository.AcquireMaterialBeforeTransactionAsync([Guid.NewGuid()], [Guid.NewGuid()], [], [Guid.NewGuid()], deadline.Token);
            Assert.IsNotNull(other); Assert.AreEqual(3L, await Locks(observer, independent));
            pending = waiter.Repository.AcquireMaterialBeforeTransactionAsync([source], [gross], [], [item], deadline.Token);
            await ObserveAttempt(observer, waiter, deadline.Token); Assert.IsFalse(pending.IsCompleted);
            Assert.AreEqual(0L, await Locks(observer, waiter)); Assert.IsNull(waiter.Session.Transaction);
            Assert.IsTrue(await TryKey(observer, "Source", source)); Assert.IsTrue(await TryKey(observer, "Gross", gross));
            await held.DisposeAsync(); var acquired = await pending; Assert.IsNotNull(acquired);
            Assert.AreEqual(3L, await Locks(observer, waiter)); await acquired.DisposeAsync(); await acquired.DisposeAsync();
            Assert.AreEqual(0L, await Locks(observer, waiter)); Assert.AreEqual(1, await waiter.Session.Connection.ExecuteScalarAsync<int>("SELECT 1"));
        }
        finally { deadline.Cancel(); await held.DisposeAsync(); await Finish(pending); }
    }

    /// <summary>等待S之前已经取得C_item，尚未取得U；取消释放部分命令项，不影响持有者的S。</summary>
    [TestMethod]
    public async Task ItemPrecedesSourceAndGross_CancellationReleasesPartialItem()
    {
        await using var holder = new Fixture(); await using var waiter = new Fixture(); await using var observer = new Fixture();
        var source = Guid.NewGuid(); var gross = Guid.NewGuid(); var item = Guid.NewGuid();
        var held = await holder.Repository.AcquireBeforeTransactionAsync([source], []); Assert.IsNotNull(held);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)); Task<IAsyncDisposable?>? pending = null;
        try
        {
            pending = waiter.Repository.AcquireMaterialBeforeTransactionAsync([source], [gross], [], [item], deadline.Token);
            await ObserveLockCount(observer, waiter, 1, deadline.Token); Assert.IsFalse(pending.IsCompleted);
            Assert.IsFalse(await TryKey(observer, "CommandItem", item)); Assert.IsTrue(await TryKey(observer, "Gross", gross));
            deadline.Cancel(); await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
            await ObserveLockCount(observer, waiter, 0, CancellationToken.None);
            Assert.AreEqual(1L, await Locks(observer, holder)); Assert.IsTrue(await TryKey(observer, "CommandItem", item));
        }
        finally { deadline.Cancel(); await held.DisposeAsync(); await Finish(pending); }
    }

    /// <summary>包含Empty的多命令项按实际long键排序去重；反向输入仍先取第一项，等待第二项时尚无来源锁。</summary>
    [TestMethod]
    public async Task AllItemsAreStableAndDeduplicated_EmptyIdentityIsProtected()
    {
        await using var holder = new Fixture(); await using var waiter = new Fixture(); await using var observer = new Fixture();
        var items = new[] { Guid.Empty, Guid.NewGuid() }.OrderBy(x => Key("CommandItem", x)).ToArray();
        var source = Guid.NewGuid(); var gross = Guid.NewGuid();
        var held = await PostgreSqlSourceDemandLock.AcquireMaterialBeforeTransactionAsync(holder.Session, [items[1]], [], [], NullLogger.Instance, CancellationToken.None);
        Assert.IsNotNull(held); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)); Task<IAsyncDisposable?>? pending = null;
        try
        {
            pending = PostgreSqlSourceDemandLock.AcquireMaterialBeforeTransactionAsync(waiter.Session,
                [items[1], items[0], items[1], items[0]], [source], [gross], NullLogger.Instance, deadline.Token);
            await ObserveLockCount(observer, waiter, 1, deadline.Token); Assert.IsFalse(pending.IsCompleted);
            Assert.IsFalse(await TryKey(observer, "CommandItem", Guid.Empty));
            Assert.IsTrue(await TryKey(observer, "Source", source)); Assert.IsTrue(await TryKey(observer, "Gross", gross));
            await held.DisposeAsync(); var acquired = await pending; Assert.IsNotNull(acquired);
            Assert.AreEqual(4L, await Locks(observer, waiter)); await acquired.DisposeAsync();
            Assert.AreEqual(0L, await Locks(observer, waiter)); Assert.IsTrue(await TryKey(observer, "CommandItem", Guid.Empty));
        }
        finally { deadline.Cancel(); await held.DisposeAsync(); await Finish(pending); }
    }

    /// <summary>拒绝持需求资源后追加C_cmd，原租约不受影响；正确前缀可以共存，父事务中不能新增准备。</summary>
    [TestMethod]
    public async Task CommandAndMaterialBoundary_PreservesOrderAndCallerTransaction()
    {
        await using var f = new Fixture(); await using var observer = new Fixture();
        var receipts = new PostgreSqlCommandReceiptRepository(f.Context);
        var source = await f.Repository.AcquireMaterialBeforeTransactionAsync([], [], [], [Guid.NewGuid()]); Assert.IsNotNull(source);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => receipts.AcquireBeforeTransactionAsync(Guid.NewGuid(), CancellationToken.None));
            Assert.AreEqual(1L, await Locks(observer, f));
        }
        finally { await source.DisposeAsync(); }
        await using var command = await receipts.AcquireBeforeTransactionAsync(Guid.NewGuid(), CancellationToken.None); Assert.IsNotNull(command);
        var material = await f.Repository.AcquireMaterialBeforeTransactionAsync([Guid.NewGuid()], [Guid.NewGuid()], [], [Guid.NewGuid()]); Assert.IsNotNull(material);
        try
        {
            Assert.AreEqual(4L, await Locks(observer, f));
            f.Session.Transaction = f.Session.Connection.BeginTransaction(IsolationLevel.Serializable); var transaction = f.Session.Transaction;
            try
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => f.Repository.AcquireMaterialBeforeTransactionAsync([], [], [], []));
                Assert.AreSame(transaction, f.Session.Transaction);
                Assert.AreEqual(1, await f.Session.Connection.ExecuteScalarAsync<int>("SELECT 1", transaction: transaction));
            }
            finally { f.Rollback(); }
        }
        finally { await material.DisposeAsync(); }
        Assert.AreEqual(1L, await Locks(observer, f)); await command.DisposeAsync(); Assert.AreEqual(0L, await Locks(observer, f));
    }

    /// <summary>取消关系与软删回退Net仍参与身份发现，全部同源Gross包括关闭/软删成员都受U保护，不在准备阶段做业务资格判定。</summary>
    [TestMethod]
    public async Task CancelledItemAndHistoricalNet_DiscoverAllSourceGrossMembers()
    {
        await using var f = new Fixture(); await using var observer = new Fixture();
        var first = Gross(); var sibling = Gross(first.SourceUid); sibling.IsDeleted = true; sibling.LifecycleStatus = GrossDemandLifecycleStatusEnum.Closed;
        var net = Net(first.Uid); net.IsDeleted = true; net.LifecycleStatus = NetDemandLifecycleStatusEnum.RolledBack;
        var link = Link(net.Uid, Guid.NewGuid());
        await f.Insert(first); await f.Insert(sibling); await f.Insert(net); await f.Insert(link);
        await using var lease = await f.Repository.AcquireMaterialBeforeTransactionAsync([], [], [], [link.CommandItemId]); Assert.IsNotNull(lease);
        Assert.AreEqual(4L, await Locks(observer, f)); Assert.IsFalse(await TryKey(observer, "CommandItem", link.CommandItemId));
        Assert.IsFalse(await TryKey(observer, "Source", first.SourceUid));
        Assert.IsFalse(await TryKey(observer, "Gross", first.Uid)); Assert.IsFalse(await TryKey(observer, "Gross", sibling.Uid));
        Assert.IsTrue(await TryKey(observer, "Gross", Guid.NewGuid())); Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>真实维护在等待命令项期间改Net归属；准备重验后释放旧S/U再获取新集合，只有新Gross受保护，未重复任何业务委托。</summary>
    [TestMethod]
    public async Task NetBindingChangesDuringItemWait_ReleasesAndRediscoversWholeSet()
    {
        await using var seed = new Fixture(); await using var holder = new Fixture(); await using var waiter = new Fixture(); await using var observer = new Fixture();
        var oldGross = Gross(); var newGross = Gross(); var net = Net(oldGross.Uid); var item = Guid.NewGuid();
        await seed.Insert(oldGross); await seed.Insert(newGross); await seed.Insert(net);
        var held = await PostgreSqlSourceDemandLock.AcquireMaterialBeforeTransactionAsync(holder.Session, [item], [], [], NullLogger.Instance, CancellationToken.None);
        Assert.IsNotNull(held); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)); Task<IAsyncDisposable?>? pending = null;
        try
        {
            pending = waiter.Repository.AcquireMaterialBeforeTransactionAsync([], [], [net.Uid], [item], deadline.Token);
            await ObserveAttempt(observer, waiter, deadline.Token); Assert.IsFalse(pending.IsCompleted); Assert.AreEqual(0L, await Locks(observer, waiter));
            await using (var maintenance = await seed.Repository.AcquireBeforeTransactionAsync([], [oldGross.Uid, newGross.Uid], deadline.Token))
            {
                Assert.IsNotNull(maintenance);
                Assert.AreEqual(1, await seed.Session.Connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE public.net_demand SET gross_demand_id=@Gross WHERE uid=@Uid", new { Gross = newGross.Uid, net.Uid }, cancellationToken: deadline.Token)));
            }
            await held.DisposeAsync(); var acquired = await pending; Assert.IsNotNull(acquired);
            try
            {
                Assert.AreEqual(3L, await Locks(observer, waiter)); Assert.IsTrue(await TryKey(observer, "Source", oldGross.SourceUid));
                Assert.IsTrue(await TryKey(observer, "Gross", oldGross.Uid)); Assert.IsFalse(await TryKey(observer, "Source", newGross.SourceUid));
                Assert.IsFalse(await TryKey(observer, "Gross", newGross.Uid)); Assert.IsNull(waiter.Session.Transaction);
            }
            finally { await acquired.DisposeAsync(); }
            Assert.AreEqual(0L, await Locks(observer, waiter));
        }
        finally { deadline.Cancel(); await held.DisposeAsync(); await Finish(pending); }
    }

    /// <summary>首次发现没有关系、等待后另一来源已占用该命令项；重新发现保留已取消身份及原来源保护，不能把查重范围按新来源分区。</summary>
    [TestMethod]
    public async Task ItemAppearsDuringWait_RediscoversOriginalSourceAndNewRequestResources()
    {
        await using var seed = new Fixture(); await using var holder = new Fixture(); await using var waiter = new Fixture(); await using var observer = new Fixture();
        var original = Gross(); var net = Net(original.Uid); var item = Guid.NewGuid();
        var newSource = Guid.NewGuid(); var newGross = Guid.NewGuid();
        await seed.Insert(original); await seed.Insert(net);
        var held = await holder.Repository.AcquireMaterialBeforeTransactionAsync([original.SourceUid], [original.Uid], [], [item]); Assert.IsNotNull(held);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)); Task<IAsyncDisposable?>? pending = null;
        try
        {
            pending = waiter.Repository.AcquireMaterialBeforeTransactionAsync([newSource], [newGross], [], [item], deadline.Token);
            await ObserveAttempt(observer, waiter, deadline.Token); Assert.IsFalse(pending.IsCompleted); Assert.AreEqual(0L, await Locks(observer, waiter));
            await holder.Insert(Link(net.Uid, item)); await held.DisposeAsync();
            var acquired = await pending; Assert.IsNotNull(acquired);
            try
            {
                Assert.AreEqual(5L, await Locks(observer, waiter));
                Assert.IsFalse(await TryKey(observer, "CommandItem", item)); Assert.IsFalse(await TryKey(observer, "Source", original.SourceUid));
                Assert.IsFalse(await TryKey(observer, "Gross", original.Uid)); Assert.IsFalse(await TryKey(observer, "Source", newSource));
                Assert.IsFalse(await TryKey(observer, "Gross", newGross)); Assert.IsNull(waiter.Session.Transaction);
            }
            finally { await acquired.DisposeAsync(); }
            Assert.AreEqual(0L, await Locks(observer, waiter));
        }
        finally { deadline.Cancel(); await held.DisposeAsync(); await Finish(pending); }
    }

    /// <summary>原约束允许的非标来源随机身份，不使用既有用户、单号序列或历史业务数据。</summary>
    private static GrossDemand Gross(Guid? source = null)
        => new() { Uid = Guid.NewGuid(), Version = 1, SourceType = "MD", SourceUid = source ?? Guid.NewGuid(), SourceLineId = Guid.NewGuid(),
            SourceNo = "PGMP" + Guid.NewGuid().ToString("N"), IsNonStandard = true, SkuSnapshot = "PGMP", DemandQty = 10m };
    /// <summary>合法标准交付的随机净需求根行，保留正修订、行号与数量约束。</summary>
    private static NetDemand Net(Guid gross)
    { var uid = Guid.NewGuid(); return new() { Uid = uid, Version = 1, GrossDemandId = gross, RootNetDemandId = uid, DecompositionRevision = 1,
        LineNumber = 1, GrossQty = 1m, NetQty = 1m, SupplyTypeSnapshot = "PURCHASE", SkuSnapshot = "PGMP" }; }
    /// <summary>取消关系已完全释放但命令项仍永久参与唯一身份，目标只使用本例随机逻辑引用。</summary>
    private static DemandExecutionLink Link(Guid net, Guid item)
        => new() { Uid = Guid.NewGuid(), Version = 1, NetDemandId = net, CommandItemId = item, ExecutionRequestId = Guid.NewGuid(),
            TargetType = "PO", TargetHeadId = Guid.NewGuid(), TargetLineId = Guid.NewGuid(), TargetNo = "PGMP", AllocatedQty = 1m,
            ReleasedQty = 1m, LinkStatus = DemandExecutionLinkStatusEnum.Cancelled };
    /// <summary>独立计算冻结资源键只构造争用和目录验证，不取代实际生产准备。</summary>
    private static long Key(string family, Guid uid) => BinaryPrimitives.ReadInt64BigEndian(
        SHA256.HashData(Encoding.UTF8.GetBytes($"SwiftExport:SourceDemand:{family}:{uid:N}")));
    /// <summary>第三物理连接实际尝试已知键，成功立即对称归还，避免测试观察者遗留计数。</summary>
    private static async Task<bool> TryKey(Fixture observer, string family, Guid uid)
    { var key = Key(family, uid); var ok = await observer.Session.Connection.ExecuteScalarAsync<bool>("SELECT pg_try_advisory_lock(@Key)", new { Key = key });
        if (ok) Assert.IsTrue(await observer.Session.Connection.ExecuteScalarAsync<bool>("SELECT pg_advisory_unlock(@Key)", new { Key = key })); return ok; }
    /// <summary>服务器PID目录计数，而非逻辑Task状态充获取证据。</summary>
    private static Task<long> Locks(Fixture observer, Fixture target) => observer.Session.Connection.ExecuteScalarAsync<long>(
        "SELECT count(*) FROM pg_locks WHERE pid=@Pid AND locktype='advisory'", new { target.Pid });
    /// <summary>观察生产锁SQL已实际到达服务器且未分配父事务ID。</summary>
    private static async Task ObserveAttempt(Fixture observer, Fixture target, CancellationToken token)
    { while (!await observer.Session.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
        "SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid=@Pid AND query LIKE '%pg_try_advisory_lock%' AND backend_xid IS NULL)", new { target.Pid }, cancellationToken: token)))
        await Task.Delay(25, token); }
    /// <summary>有界轮询真实部分持锁及取消后的零锁，固定延时不是并发证据。</summary>
    private static async Task ObserveLockCount(Fixture observer, Fixture target, long expected, CancellationToken token)
    { using var safety = CancellationTokenSource.CreateLinkedTokenSource(token); safety.CancelAfter(TimeSpan.FromSeconds(8));
        while (await Locks(observer, target) != expected) await Task.Delay(25, safety.Token); }
    /// <summary>测试异常也等待晚到租约完成清理，不遗留正在访问已释放Session的后台工作。</summary>
    private static async Task Finish(Task<IAsyncDisposable?>? pending)
    { if (pending is null) return; try { if (await pending is { } late) await late.DisposeAsync(); } catch (OperationCanceledException) { } }

    /// <summary>严格故障库与真实审计上下文；只有两项历史/改绑用例需要提交随机三表夹具，按精确身份逆依赖清理。</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly Dictionary<Type, HashSet<Guid>> identities = [];
        public DbSession Session { get; } = new(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        public int Pid { get; }
        public RepositoryBaseContext Context { get; }
        public PostgreSqlGrossDemandDocumentRepository Repository { get; }
        /// <summary>记录真实PID并复用同一context；不创建业务用户或新生产接口。</summary>
        public Fixture()
        { Pid = ((NpgsqlConnection)Session.Connection).ProcessID; var user = new User();
            Context = new(Session, NullLoggerFactory.Instance, new AuditStampService(user), user); Repository = new(Context); }
        /// <summary>写前登记精确UID，完整元数据参数保留合法状态和nullable字段；本例初始化自动提交便于独立连接可见。</summary>
        public async Task Insert<T>(T row) where T : BaseEntity
        {
            if (!identities.TryGetValue(typeof(T), out var ids)) identities[typeof(T)] = ids = [];
            Assert.AreNotEqual(Guid.Empty, row.Uid); ids.Add(row.Uid); var map = PostgreSqlEntityMetadata.GetMetadata(typeof(T));
            var table = PostgreSqlNames.Quote(map.SchemaName) + "." + PostgreSqlNames.Quote(map.TableName);
            Assert.AreEqual(1, await Session.Connection.ExecuteAsync(new CommandDefinition(
                $"INSERT INTO {table} ({string.Join(',', map.Columns.Select(c => PostgreSqlNames.Quote(c.ColumnName)))}) VALUES ({string.Join(',', map.Columns.Select(c => "@" + c.ParameterName))})",
                PostgreSqlParameters.Entity(row, map))));
        }
        /// <summary>先结束真实父事务，再释放来源与命令租约；仅本例自有事务回滚。</summary>
        public void Rollback()
        { if (Session.Transaction is not { } transaction) return; try { transaction.Rollback(); } finally { Session.Transaction = null; transaction.Dispose(); } }
        /// <summary>失败路径也清本例随机Link→Net→Gross，不删除来源范围或重置数据库；独立清理连接可处理原会话被隔离。</summary>
        public async ValueTask DisposeAsync()
        {
            try
            {
                Rollback(); if (Session.SourceDemandLease is { } source) await source.DisposeAsync();
                if (Session.CommandLease is { } command) await command.DisposeAsync();
                if (identities.Count == 0) return;
                using var cleanup = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
                foreach (var type in new[] { typeof(DemandExecutionLink), typeof(NetDemand), typeof(GrossDemand) })
                {
                    if (!identities.TryGetValue(type, out var ids)) continue; var map = PostgreSqlEntityMetadata.GetMetadata(type);
                    var table = PostgreSqlNames.Quote(map.SchemaName) + "." + PostgreSqlNames.Quote(map.TableName);
                    await cleanup.Connection.ExecuteAsync(new CommandDefinition($"DELETE FROM {table} WHERE uid=ANY(@Uids)", PostgreSqlParameters.Ids(ids), commandTimeout: 10));
                    Assert.AreEqual(0L, await cleanup.Connection.ExecuteScalarAsync<long>(new CommandDefinition($"SELECT count(*) FROM {table} WHERE uid=ANY(@Uids)", PostgreSqlParameters.Ids(ids))));
                }
            }
            finally { Session.Dispose(); }
        }
    }
    /// <summary>固定测试审计身份，不读取真实账户或凭据。</summary>
    private sealed class User : ICurrentUser
    { public Guid? UserId => null; public string UserName => "__pgtest_material_preparation__"; public bool IsAuthenticated => false; }
}
