using Dapper;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Models.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.DemandPlanning.Materials.SupplierSuggestion;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Commands;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Commands;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Commands;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Document;
using SwiftExport.Infrastructure.Repositories.Suppliers.Management;
using SwiftExport.Infrastructure.Services.Auth;
using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SwiftExport.Application.Tests.Database;

/// <summary>真实服务与PG端口验证推荐供应商命令；成功保留随机事实及不可变回执，失败仅精确清本例零回执夹具。</summary>
[TestClass, DoNotParallelize]
public sealed class PostgreSqlMaterialSupplierSuggestionServiceTests
{
    /// <summary>输出无凭据UID与保留策略，供主控实际运行后逐例核对。</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>原同值不写版本；清空和更换只写源码实际四列，回执与事实在同一真实事务提交。</summary>
    [TestMethod]
    [DataRow("same")]
    [DataRow("clear")]
    [DataRow("replace")]
    public async Task SuccessfulCommand_PreservesLocalColumnsAndCommitsReceipt(string mode)
    {
        await using var fixture = await Create(keep: true); using var session = fixture.Open(); using var host = new Host(session, fixture.Actor);
        var before = await Read<NetDemand>(session, fixture.Net.Uid);
        var grossBefore = await Read<GrossDemand>(session, fixture.Gross.Uid);
        Guid? suggestion = mode == "clear" ? null : mode == "same" ? fixture.Suppliers[0].Uid : fixture.SecondSupplier();
        var command = fixture.Command(suggestion);
        var result = await host.Service.UpdateSuggestedPurchaseSupplierAsync(command);
        var value = Success(result);
        Assert.AreEqual(mode == "same" ? 5 : 6, value.Version); Assert.AreEqual(suggestion, value.SuggestedPurchaseSupplierId);
        var after = await Read<NetDemand>(session, fixture.Net.Uid);
        Compare(before, after, mode == "same" ? [] : ["Version", "SuggestedPurchaseSupplierId", "UpdatedDate", "UpdatedBy"]);
        Assert.AreEqual(value.Version, after.Version); Assert.AreEqual(suggestion, after.SuggestedPurchaseSupplierId);
        if (mode != "same") { Assert.AreEqual(host.User.UserName, after.UpdatedBy); Assert.IsNotNull(after.UpdatedDate); }
        if (suggestion is null) { Assert.AreEqual("", value.SuggestedPurchaseSupplierCode); Assert.AreEqual("", value.SuggestedPurchaseSupplierName); }
        else
        {
            var supplier = fixture.Suppliers.Single(x => x.Uid == suggestion);
            Assert.AreEqual(supplier.Code, value.SuggestedPurchaseSupplierCode); Assert.AreEqual(supplier.Name, value.SuggestedPurchaseSupplierName);
        }
        Compare(grossBefore, await Read<GrossDemand>(session, fixture.Gross.Uid), []);
        var receipt = await host.Receipts.FindAsync(command.CommandId, default); Assert.IsNotNull(receipt);
        Assert.AreEqual(command.NetDemandId, receipt.RequestTargetUid); Assert.AreEqual(fixture.Actor, receipt.ActorUid);
        Assert.AreEqual(1, await fixture.ReceiptCount(session)); Assert.AreEqual(value.Version, JsonSerializer.Deserialize<UpdateSuggestedPurchaseSupplierResult>(receipt.ResultJson)!.Version);
        Released(session);
    }

    /// <summary>既有停用但未删供应商按原GetById仍有效，不为了错误文案增加IsActive拒绝；无此主数据便未运行。</summary>
    [TestMethod]
    public async Task ExistingInactiveSupplier_IsStillAcceptedByOriginalReadPolicy()
    {
        await using var fixture = await Create(keep: true, inactive: true); using var session = fixture.Open(); using var host = new Host(session, fixture.Actor);
        var command = fixture.Command(fixture.Suppliers[0].Uid);
        Assert.AreEqual(5, Success(await host.Service.UpdateSuggestedPurchaseSupplierAsync(command)).Version);
        Assert.AreEqual(1, await fixture.ReceiptCount(session)); Released(session);
    }

    /// <summary>独立连接重放先于后续版本/生命周期；内容、actor冲突与权限拒绝不覆盖原不可变回执。</summary>
    [TestMethod]
    public async Task ReplayConflictAndPermission_PreserveOriginalReceipt()
    {
        await using var fixture = await Create(keep: true); using var first = fixture.Open(); using var firstHost = new Host(first, fixture.Actor);
        var command = fixture.Command(null); var original = Success(await firstHost.Service.UpdateSuggestedPurchaseSupplierAsync(command));
        var receipt = await firstHost.Receipts.FindAsync(command.CommandId, default); Assert.IsNotNull(receipt);
        await Maintain(first, fixture, close: true, increment: true);
        using var second = fixture.Open(); using var host = new Host(second, fixture.Actor);
        Assert.AreNotEqual(Pid(first), Pid(second));
        var replay = Success(await host.Service.UpdateSuggestedPurchaseSupplierAsync(command));
        Assert.AreEqual(original.Version, replay.Version); Assert.IsNull(replay.SuggestedPurchaseSupplierId);
        Assert.AreEqual(7, (await Read<NetDemand>(second, fixture.Net.Uid)).Version);
        var changed = fixture.Command(fixture.Suppliers[0].Uid, command.CommandId);
        Assert.AreEqual(ServiceErrorCodes.CommandConflict, (await host.Service.UpdateSuggestedPurchaseSupplierAsync(changed)).Error?.Code);
        host.User.UserId = Guid.NewGuid();
        Assert.AreEqual(ServiceErrorCodes.CommandConflict, (await host.Service.UpdateSuggestedPurchaseSupplierAsync(command)).Error?.Code);
        host.User.UserId = fixture.Actor; host.Permission.Allow = false;
        Assert.AreEqual(ServiceErrorCodes.Forbidden, (await host.Service.UpdateSuggestedPurchaseSupplierAsync(command)).Error?.Code);
        Compare(receipt, (await host.Receipts.FindAsync(command.CommandId, default))!, []);
        Assert.AreEqual(1, await fixture.ReceiptCount(second)); Released(first); Released(second);
    }

    /// <summary>原业务/权限/输入拒绝没有回执或事实覆盖，失败库按UID清理，不碰主数据。</summary>
    [TestMethod]
    [DataRow("old-version", "CONCURRENCY_CONFLICT")]
    [DataRow("gross-closed", "MATERIAL_DEMAND_NOT_ACTIVE")]
    [DataRow("gross-deleted", "MATERIAL_DEMAND_NOT_ACTIVE")]
    [DataRow("net-cancelled", "MATERIAL_DEMAND_NOT_ACTIVE")]
    [DataRow("net-deleted", "MATERIAL_DEMAND_NOT_ACTIVE")]
    [DataRow("missing-net", "MATERIAL_DEMAND_NOT_ACTIVE")]
    [DataRow("missing-supplier", "INVALID_SUPPLIER")]
    [DataRow("empty-supplier", "INVALID_SUPPLIER")]
    [DataRow("empty-net", "INVALID_COMMAND")]
    [DataRow("bad-version", "INVALID_COMMAND")]
    [DataRow("empty-command", "VALIDATION")]
    [DataRow("null-command", "VALIDATION")]
    [DataRow("forbidden", "FORBIDDEN")]
    [DataRow("unauthenticated", "UNAUTHENTICATED")]
    public async Task RejectedCommand_HasNoReceiptOrFactChange(string kind, string expected)
    {
        await using var fixture = await Create(keep: false, shape: kind); using var session = fixture.Open(); using var host = new Host(session, fixture.Actor);
        var before = await Read<NetDemand>(session, fixture.Net.Uid); var grossBefore = await Read<GrossDemand>(session, fixture.Gross.Uid);
        var command = fixture.Command(null);
        switch (kind)
        {
            case "old-version": command.Version = 4; break;
            case "missing-net": command.NetDemandId = Guid.NewGuid(); break;
            case "missing-supplier": command.SuggestedPurchaseSupplierId = Guid.NewGuid(); break;
            case "empty-supplier": command.SuggestedPurchaseSupplierId = Guid.Empty; break;
            case "empty-net": command.NetDemandId = Guid.Empty; break;
            case "bad-version": command.Version = 0; break;
            case "empty-command": command.CommandId = Guid.Empty; break;
            case "forbidden": host.Permission.Allow = false; break;
            case "unauthenticated": host.User.IsAuthenticated = false; break;
        }
        var result = await host.Service.UpdateSuggestedPurchaseSupplierAsync(kind == "null-command" ? null! : command);
        Assert.IsFalse(result.Success); Assert.AreEqual(expected, result.Error?.Code);
        Compare(before, await Read<NetDemand>(session, fixture.Net.Uid), []);
        Compare(grossBefore, await Read<GrossDemand>(session, fixture.Gross.Uid), []);
        Assert.AreEqual(0, await fixture.ReceiptCount(session)); Released(session);
    }

    /// <summary>Net已写且真实回执Insert已执行后注入故障，Executor回滚两者；窄端口其他操作及原生准备均真实委托。</summary>
    [TestMethod]
    public async Task ReceiptFailureAfterFact_RollsBackRealParentTransaction()
    {
        await using var fixture = await Create(keep: false); using var session = fixture.Open();
        var before = await Read<NetDemand>(session, fixture.Net.Uid);
        var hook = new ReceiptHook(new PostgreSqlCommandReceiptRepository(Context(session, new User(fixture.Actor))))
        {
            AfterInsert = async _ => { Assert.AreEqual(6, (await Read<NetDemand>(session, fixture.Net.Uid)).Version); throw new IOException("本例回执后故障"); }
        };
        using var host = new Host(session, fixture.Actor, hook);
        var result = await host.Service.UpdateSuggestedPurchaseSupplierAsync(fixture.Command(null));
        Assert.IsFalse(result.Success); Assert.AreEqual(ServiceErrorCodes.Unexpected, result.Error?.Code); Assert.AreEqual(1, hook.Inserts);
        using var observer = fixture.Open(); Compare(before, await Read<NetDemand>(observer, fixture.Net.Uid), []);
        Assert.AreEqual(0, await fixture.ReceiptCount(observer)); Released(session);
    }

    /// <summary>真实来源键在首次业务事务前等待；owner版本/关闭提交后候选按原错误拒绝，旧版本维护也不能覆盖owner。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SourceWait_FirstBusinessSnapshotSeesOwnerCommit(bool close)
    {
        await using var fixture = await Create(keep: false); using var owner = fixture.Open(); using var candidate = fixture.Open(); using var observer = fixture.Open();
        var ownerNet = new PostgreSqlMaterialDemandDocumentRepository(Context(owner, new User(fixture.Actor)));
        var lease = await ownerNet.AcquireMaterialBeforeTransactionAsync([], [], [fixture.Net.Uid], []); Assert.IsNotNull(lease);
        Task<ServiceResult<UpdateSuggestedPurchaseSupplierResult>>? pending = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); using var host = new Host(candidate, fixture.Actor);
        try
        {
            using var transaction = owner.Connection.BeginTransaction(IsolationLevel.ReadCommitted); owner.Transaction = transaction;
            try
            {
                await OwnerChange(owner, fixture, close, increment: true);
                pending = host.Service.UpdateSuggestedPurchaseSupplierAsync(fixture.Command(null), timeout.Token);
                await ObserveWait(observer, candidate, owner, SourceKey(fixture.Gross.SourceUid), timeout.Token);
                Assert.IsNull(candidate.Transaction); Assert.IsTrue(candidate.CommandLease!.Held);
                transaction.Commit();
            }
            finally { owner.Transaction = null; }
            await lease.DisposeAsync();
            var result = await pending; Assert.IsFalse(result.Success);
            Assert.AreEqual(close ? "MATERIAL_DEMAND_NOT_ACTIVE" : "CONCURRENCY_CONFLICT", result.Error?.Code);
            var row = await Read<NetDemand>(observer, fixture.Net.Uid);
            Assert.AreEqual(6, row.Version); Assert.AreEqual(fixture.Suppliers[0].Uid, row.SuggestedPurchaseSupplierId);
            Assert.AreEqual(0, await fixture.ReceiptCount(observer)); Released(candidate);
            // owner再按过时版本写建议只能得到零；不重试业务，不改变服务隔离。
            var late = await ownerNet.AcquireMaterialBeforeTransactionAsync([], [], [fixture.Net.Uid], []); Assert.IsNotNull(late);
            await using (late)
            {
                using var lateTransaction = owner.Connection.BeginTransaction(IsolationLevel.ReadCommitted); owner.Transaction = lateTransaction;
                try { Assert.AreEqual(0, await ownerNet.UpdateSuggestedPurchaseSupplierAsync(fixture.Net.Uid, 5, null)); lateTransaction.Rollback(); }
                finally { owner.Transaction = null; }
            }
        }
        finally { owner.Transaction = null; await lease.DisposeAsync(); if (pending is not null) { try { await pending; } catch (OperationCanceledException) { } } }
    }

    /// <summary>两个真实backend同Command只产生一次事实/回执；第二个在事务前命令键等待并返回原结果。</summary>
    [TestMethod]
    public async Task ConcurrentSameCommand_CommitsOneReceiptAndOneVersionChange()
    {
        await using var fixture = await Create(keep: true); using var owner = fixture.Open(); using var candidate = fixture.Open(); using var observer = fixture.Open();
        var arrived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var hook = new ReceiptHook(new PostgreSqlCommandReceiptRepository(Context(owner, new User(fixture.Actor))))
        { BeforeInsert = async token => { arrived.TrySetResult(true); await release.Task.WaitAsync(token); } };
        using var firstHost = new Host(owner, fixture.Actor, hook); using var secondHost = new Host(candidate, fixture.Actor);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var command = fixture.Command(null); var first = firstHost.Service.UpdateSuggestedPurchaseSupplierAsync(command, timeout.Token);
        Task<ServiceResult<UpdateSuggestedPurchaseSupplierResult>>? second = null;
        try
        {
            await arrived.Task.WaitAsync(timeout.Token);
            second = secondHost.Service.UpdateSuggestedPurchaseSupplierAsync(command, timeout.Token);
            await ObserveWait(observer, candidate, owner, CommandKey(command.CommandId), timeout.Token);
            Assert.IsNull(candidate.Transaction); release.TrySetResult(true);
            Assert.AreEqual(6, Success(await first).Version); Assert.AreEqual(6, Success(await second).Version);
            Assert.AreEqual(6, (await Read<NetDemand>(observer, fixture.Net.Uid)).Version);
            Assert.AreEqual(1, await fixture.ReceiptCount(observer)); Assert.AreEqual(1, hook.Inserts); Released(owner); Released(candidate);
        }
        finally { release.TrySetResult(true); try { await first; if (second is not null) await second; } catch (OperationCanceledException) { } }
    }

    /// <summary>取消真实来源准备，不进入业务事务且无回执；归还后第三连接能重新取得命令及来源资源。</summary>
    [TestMethod]
    public async Task CancelledPreparation_ReleasesResourcesForThirdBackend()
    {
        await using var fixture = await Create(keep: false); using var owner = fixture.Open(); using var candidate = fixture.Open(); using var observer = fixture.Open();
        var net = new PostgreSqlMaterialDemandDocumentRepository(Context(owner, new User(fixture.Actor)));
        var lease = await net.AcquireMaterialBeforeTransactionAsync([], [], [fixture.Net.Uid], []); Assert.IsNotNull(lease);
        using var cancel = new CancellationTokenSource(); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var host = new Host(candidate, fixture.Actor); var command = fixture.Command(null);
        var pending = host.Service.UpdateSuggestedPurchaseSupplierAsync(command, cancel.Token);
        try
        {
            await ObserveWait(observer, candidate, owner, SourceKey(fixture.Gross.SourceUid), deadline.Token);
            Assert.IsNull(candidate.Transaction); cancel.Cancel();
            // 准备层使用关联令牌；验证调用方取消继续传播，不要求异常携带同一个令牌实例。
            var error = await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
            Assert.IsTrue(cancel.IsCancellationRequested); Assert.IsTrue(error.CancellationToken.IsCancellationRequested);
            Released(candidate); Assert.AreEqual(0, await fixture.ReceiptCount(observer));
            Assert.AreEqual(5, (await Read<NetDemand>(observer, fixture.Net.Uid)).Version);
            await lease.DisposeAsync();
            using var third = fixture.Open(); var context = Context(third, new User(fixture.Actor));
            await using var commandLease = await new PostgreSqlCommandReceiptRepository(context).AcquireBeforeTransactionAsync(command.CommandId, deadline.Token);
            Assert.IsNotNull(commandLease);
            await using var sourceLease = await new PostgreSqlMaterialDemandDocumentRepository(context).AcquireMaterialBeforeTransactionAsync([], [], [fixture.Net.Uid], [], deadline.Token);
            Assert.IsNotNull(sourceLease); Assert.IsTrue(third.SourceDemandLease!.Covers("Gross", fixture.Gross.Uid));
        }
        finally { cancel.Cancel(); await lease.DisposeAsync(); try { await pending; } catch (OperationCanceledException) { } }
    }

    /// <summary>严格选库先于任何连接；成功保留RBAC事实，失败只能在failure零回执后清理。</summary>
    private async Task<Fixture> Create(bool keep, string shape = "", bool inactive = false)
    {
        var fixture = new Fixture(keep ? CommittedConnection() : PostgreSqlIntegrationEnvironment.GetFailureConnectionString(), keep,
            json => TestContext.WriteLine(json));
        try { await fixture.Initialize(shape, inactive); return fixture; }
        catch { await fixture.DisposeAsync(); throw; }
    }
    /// <summary>只允许已存在的本批RBAC库，未配置门禁则未运行，不读取命令测试库或默认凭据。</summary>
    private static string CommittedConnection()
    {
        var text = Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RBAC_TEST_CONNECTION");
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_PG_RBAC_TESTS") != "1" || string.IsNullOrWhiteSpace(text)) Assert.Inconclusive("未启用推荐供应商提交测试；本项未运行。");
        NpgsqlConnectionStringBuilder settings;
        try { settings = new(text); } catch (ArgumentException) { Assert.Fail("本批专用连接格式无效。"); throw; }
        Assert.AreEqual("192.168.3.12", settings.Host); Assert.AreEqual(5432, settings.Port); Assert.AreEqual("dv_next_pg_rbac_tests_20261005", settings.Database);
        Assert.IsTrue(string.IsNullOrEmpty(settings.SearchPath) || settings.SearchPath == "public");
        Assert.IsFalse(settings.Multiplexing); Assert.IsFalse(settings.NoResetOnClose); return text!;
    }
    /// <summary>仓储与服务共享同一真实session、审计与明确当前用户。</summary>
    private static RepositoryBaseContext Context(DbSession session, User user) => new(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
    /// <summary>读取本例全列快照，映射字段不遗漏；可借用owner父事务作故障屏障取证。</summary>
    private static Task<T> Read<T>(DbSession session, Guid uid) where T : BaseEntity
    {
        var map = EntityMetadataCache.GetMetadata(typeof(T));
        return session.Connection.QuerySingleAsync<T>(new CommandDefinition($"SELECT {string.Join(',', map.Columns.Select(x => PostgreSqlNames.Quote(x.ColumnName) + " AS " + PostgreSqlNames.Quote(x.PropertyName)))} FROM public.{PostgreSqlNames.Quote(map.TableName)} WHERE uid=@Uid", new { Uid = uid }, session.Transaction));
    }
    /// <summary>仅排除SQL实际写列，其余快照逐属性比较，避免未检查字段被覆盖。</summary>
    private static void Compare<T>(T before, T after, string[] allowed) where T : BaseEntity
    { foreach (var column in EntityMetadataCache.GetMetadata(typeof(T)).Columns.Where(x => !allowed.Contains(x.PropertyName))) Assert.AreEqual(column.Property.GetValue(before), column.Property.GetValue(after), column.PropertyName); }
    /// <summary>成功必须有真实结果，不能以空DTO替代服务成功。</summary>
    private static UpdateSuggestedPurchaseSupplierResult Success(ServiceResult<UpdateSuggestedPurchaseSupplierResult> result)
    { Assert.IsTrue(result.Success, result.Message); Assert.IsNotNull(result.Data); return result.Data; }
    /// <summary>执行器完成或取消后必须清理父事务与两类真实租约。</summary>
    private static void Released(DbSession session)
    { Assert.IsNull(session.Transaction); Assert.IsNull(session.CommandLease); Assert.IsNull(session.SourceDemandLease); }
    /// <summary>实际连接PID用于取证，不能只用逻辑会话对象。</summary>
    private static int Pid(DbSession session) => ((NpgsqlConnection)session.Connection).ProcessID;
    /// <summary>读取冻结真实来源键，不改生产租约或Held集合。</summary>
    private static long SourceKey(Guid uid) => BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"SwiftExport:SourceDemand:Source:{uid:N}")));
    /// <summary>读取冻结命令键，与原回执同一身份域。</summary>
    private static long CommandKey(Guid uid) => BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"SwiftExport:Command:{uid:N}")));
    /// <summary>确认实际try-lock、未进入业务事务与owner同键granted，轮询只用于目录屏障。</summary>
    private static async Task ObserveWait(DbSession observer, DbSession candidate, DbSession owner, long key, CancellationToken token)
    {
        Assert.AreNotEqual(Pid(candidate), Pid(owner)); var bits = unchecked((ulong)key);
        while (!await observer.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid=@Candidate AND backend_xid IS NULL AND query LIKE '%pg_try_advisory_lock%')
               AND EXISTS(SELECT 1 FROM pg_locks WHERE pid=@Owner AND locktype='advisory' AND granted AND objsubid=1 AND classid::bigint=@High AND objid::bigint=@Low)
            """, new { Candidate = Pid(candidate), Owner = Pid(owner), High = (long)(bits >> 32), Low = (long)(bits & uint.MaxValue) }, cancellationToken: token))) await Task.Delay(20, token);
    }
    /// <summary>原生来源准备后的合法owner维护，模拟竞争提交，不调用命令以免生成额外回执。</summary>
    private static async Task Maintain(DbSession session, Fixture fixture, bool close, bool increment)
    {
        await using var lease = await new PostgreSqlMaterialDemandDocumentRepository(Context(session, new User(fixture.Actor)))
            .AcquireMaterialBeforeTransactionAsync([], [], [fixture.Net.Uid], []); Assert.IsNotNull(lease);
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted); session.Transaction = transaction;
        try { await OwnerChange(session, fixture, close, increment); transaction.Commit(); }
        finally { session.Transaction = null; }
    }
    /// <summary>只修改本例已登记owner字段；调用方负责真实事务和资源，原建议与其他快照不动。</summary>
    private static async Task OwnerChange(DbSession session, Fixture fixture, bool close, bool increment)
    {
        if (increment) Assert.AreEqual(1, await session.Connection.ExecuteAsync(new CommandDefinition("UPDATE public.net_demand SET version=version+1 WHERE uid=@Uid", new { fixture.Net.Uid }, session.Transaction)));
        if (close) Assert.AreEqual(1, await session.Connection.ExecuteAsync(new CommandDefinition("UPDATE public.gross_demand SET lifecycle_status=8,version=version+1 WHERE uid=@Uid", new { fixture.Gross.Uid }, session.Transaction)));
    }

    /// <summary>组合真实服务、验证器、PG仓储和执行器；只替换明确权限/当前人/关联端口。</summary>
    private sealed class Host : IDisposable
    {
        private readonly ServiceProvider provider;
        internal User User; internal Permission Permission = new(); internal PostgreSqlCommandReceiptRepository Receipts;
        internal MaterialDemandSupplierSuggestionService Service;
        internal Host(DbSession session, Guid actor, ICommandReceiptRepository? receipts = null)
        {
            User = new(actor); var context = Context(session, User); var services = new ServiceCollection();
            services.AddSingleton<IValidator<UpdateSuggestedPurchaseSupplierCommand>>(new UpdateSuggestedPurchaseSupplierCommandValidator()); provider = services.BuildServiceProvider();
            var serviceContext = new ServiceBaseContext(session, NullLoggerFactory.Instance); Receipts = new(context);
            var pipeline = new CommandPipeline(new CommandRequestValidator(provider), new Correlation(), User, NullLogger<CommandPipeline>.Instance);
            Service = new(serviceContext, new PostgreSqlSupplierRepository(context), new PostgreSqlMaterialDemandDocumentRepository(context), User, Permission,
                pipeline, new BusinessCommandExecutor(serviceContext, receipts ?? Receipts, User));
        }
        /// <summary>释放测试验证器容器，不接管生产会话。</summary>
        public void Dispose() => provider.Dispose();
    }
    /// <summary>只在真实回执Insert前后设置屏障/故障，其余操作及命令准备原样委托。</summary>
    private sealed class ReceiptHook(PostgreSqlCommandReceiptRepository inner) : ICommandReceiptRepository, ICommandTransactionPreparation
    {
        internal Func<CancellationToken, Task>? BeforeInsert; internal Func<CancellationToken, Task>? AfterInsert; internal int Inserts;
        /// <summary>真实命令租约保留，不能因装饰端口丢失事务前能力。</summary>
        public Task<IAsyncDisposable?> AcquireBeforeTransactionAsync(Guid id, CancellationToken token) => inner.AcquireBeforeTransactionAsync(id, token);
        /// <summary>委托真实物理命令锁消费。</summary>
        public Task<int> LockAsync(Guid id, CancellationToken token) => inner.LockAsync(id, token);
        /// <summary>只读真实不可变回执。</summary>
        public Task<CommandReceipt?> FindAsync(Guid id, CancellationToken token) => inner.FindAsync(id, token);
        /// <summary>真实父事务内追加后才允许故障；本体不替代DB事实。</summary>
        public async Task InsertAsync(CommandReceipt receipt, CancellationToken token)
        { Inserts++; if (BeforeInsert is not null) await BeforeInsert(token); await inner.InsertAsync(receipt, token); if (AfterInsert is not null) await AfterInsert(token); }
    }
    /// <summary>每例提前登记随机UID和命令，成功不清不可变回执；失败有回执便拒绝清理。</summary>
    private sealed class Fixture(string connection, bool keep, Action<string> write) : IAsyncDisposable
    {
        internal Guid Actor = Guid.NewGuid(); internal Guid CommandId = Guid.NewGuid(); internal GrossDemand Gross = new(); internal NetDemand Net = new();
        internal SupplierSnapshot[] Suppliers = []; private bool initialized;
        /// <summary>任何原生连接都再次检查禁用会话复用选项，fixture不创造隐含业务事务。</summary>
        internal DbSession Open()
        {
            var settings = new NpgsqlConnectionStringBuilder(connection); Assert.IsFalse(settings.Multiplexing); Assert.IsFalse(settings.NoResetOnClose);
            return new(new PostgreSqlConnectionFactory(connection));
        }
        /// <summary>合法MD非标Gross/根Net，仅已存在供应商只读；所有UID在初始化SQL提交前输出。</summary>
        internal async Task Initialize(string shape, bool inactive)
        {
            var grossId = Guid.NewGuid(); var netId = Guid.NewGuid();
            Gross = new() { Uid = grossId, SourceUid = grossId, SourceLineId = grossId, SourceType = "MD", SourceNo = "SS-" + grossId.ToString("N"),
                IsNonStandard = true, SkuSnapshot = "SS夹具", DemandQty = 100, UnitSnapshot = "PCS", Remark = "保留来源快照" };
            Net = new() { Uid = netId, Version = 5, GrossDemandId = grossId, RootNetDemandId = netId, DecompositionRevision = 1, LineNumber = 1,
                SupplyTypeSnapshot = "采购", SkuSnapshot = "SS夹具", UnitSnapshot = "PCS", GrossQty = 100, NetQty = 100, Remark = "建议不得覆盖", CreatedDate = new DateTime(2026, 10, 5), CreatedBy = "夹具", UpdatedDate = new DateTime(2026, 10, 5), UpdatedBy = "夹具" };
            write(JsonSerializer.Serialize(new { Phase = "初始化前预登记", Database = new NpgsqlConnectionStringBuilder(connection).Database, Actor, Gross = grossId, Net = netId, Command = CommandId,
                Policy = keep ? "保留随机事实与不可变回执" : "仅零回执后精确清理本例Gross/Net", Shape = shape }));
            using var session = Open();
            Suppliers = (await session.Connection.QueryAsync<SupplierSnapshot>("SELECT uid AS \"Uid\",supplier_code AS \"Code\",supplier_name AS \"Name\" FROM public.supplier WHERE is_deleted=FALSE" +
                (inactive ? " AND is_active=FALSE" : "") + " ORDER BY uid LIMIT 2")).ToArray();
            if (Suppliers.Length == 0) Assert.Inconclusive(inactive ? "没有已存在停用未删供应商；不改主数据造测试。" : "没有合法供应商，未初始化本例。");
            Net.SuggestedPurchaseSupplierId = Suppliers[0].Uid;
            if (shape == "gross-closed") Gross.LifecycleStatus = (GrossDemandLifecycleStatusEnum)8;
            if (shape == "gross-deleted") Gross.IsDeleted = true;
            if (shape == "net-cancelled") Net.LifecycleStatus = (NetDemandLifecycleStatusEnum)8;
            if (shape == "net-deleted") Net.IsDeleted = true;
            using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted); session.Transaction = transaction;
            try { await Insert(session, Gross); await Insert(session, Net); transaction.Commit(); initialized = true; }
            finally { session.Transaction = null; }
        }
        /// <summary>更换只用第二个真实既有供应商；不足时记录夹具保留策略，不修改主数据。</summary>
        internal Guid SecondSupplier() { if (Suppliers.Length < 2) Assert.Inconclusive("更换用例需要两个已有未删供应商。"); return Suppliers[1].Uid; }
        /// <summary>同command由fixture统一预登记，重放不另造身份。</summary>
        internal UpdateSuggestedPurchaseSupplierCommand Command(Guid? supplier, Guid? command = null) => new()
        { CommandId = command ?? CommandId, NetDemandId = Net.Uid, Version = 5, SuggestedPurchaseSupplierId = supplier };
        /// <summary>按本例command计数，不用全表行数解释本批结果。</summary>
        internal Task<int> ReceiptCount(DbSession session) => session.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id", new { Id = CommandId });
        /// <summary>初始化使用元数据typed完整参数，不调用服务取号或触碰账号/日志/目录。</summary>
        private static Task<int> Insert(DbSession session, BaseEntity entity)
        {
            var map = EntityMetadataCache.GetMetadata(entity.GetType());
            return session.Connection.ExecuteAsync(new CommandDefinition($"INSERT INTO public.{PostgreSqlNames.Quote(map.TableName)} ({string.Join(',', map.Columns.Select(x => PostgreSqlNames.Quote(x.ColumnName)))}) VALUES ({string.Join(',', map.Columns.Select(x => '@' + x.ParameterName))})", new Parameters(entity, map), session.Transaction));
        }
        /// <summary>失败库先确认本例没有回执，再精确清Net/Gross；未知提交或发现回执不能扩大删除。</summary>
        public async ValueTask DisposeAsync()
        {
            if (!initialized || keep) return;
            using var session = Open(); Assert.AreEqual(0, await ReceiptCount(session), "本例有不可变回执，禁止删除事实；按预登记清单交主控核查。");
            using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted); session.Transaction = transaction;
            try
            {
                await session.Connection.ExecuteAsync(new CommandDefinition("DELETE FROM public.net_demand WHERE uid=@Uid", new { Net.Uid }, transaction));
                await session.Connection.ExecuteAsync(new CommandDefinition("DELETE FROM public.gross_demand WHERE uid=@Uid", new { Gross.Uid }, transaction));
                transaction.Commit(); initialized = false; write(JsonSerializer.Serialize(new { Phase = "零回执精确清理已提交", Gross = Gross.Uid, Net = Net.Uid, Command = CommandId }));
            }
            finally { session.Transaction = null; }
        }
    }
    /// <summary>初始化所有NULL文本显式typed，避免驱动推断；公共参数框架只读。</summary>
    private sealed class Parameters(BaseEntity entity, EntityMetadata map) : SqlMapper.IDynamicParameters
    {
        /// <summary>沿用元数据时间/枚举类型，再为可空字符串明确text。</summary>
        public void AddParameters(IDbCommand command, SqlMapper.Identity identity)
        { PostgreSqlParameters.Entity(entity, map).AddParameters(command, identity); foreach (var column in map.Columns.Where(x => x.Property.PropertyType == typeof(string))) ((NpgsqlParameter)command.Parameters[column.ParameterName]!).NpgsqlDbType = NpgsqlDbType.Text; }
    }
    /// <summary>仅读既有供应商身份与快照，不创建或修改供应商。</summary>
    private sealed class SupplierSnapshot { public Guid Uid { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; }
    /// <summary>明确测试当前人，仅审计和指纹端口，不宣称真实认证通过。</summary>
    private sealed class User(Guid actor) : ICurrentUser
    {
        /// <summary>固定可辨识审计名称。</summary>
        public string UserName => "M15-SS-TEST01";
        public Guid? UserId { get; set; } = actor; public bool IsAuthenticated { get; set; } = true;
    }
    /// <summary>固定关联标识，测试不运行HTTP宿主。</summary>
    private sealed class Correlation : ICorrelationContext
    {
        /// <summary>本批固定关联名称，不代替Command身份。</summary>
        public string CorrelationId => "M15-SS-TEST01";
    }
    /// <summary>明确权限测试端口，只核原服务请求稳定键，不使用伪造数据库事实。</summary>
    private sealed class Permission : IPermissionEvaluationService
    {
        internal bool Allow = true;
        /// <summary>未使用权限投影，误调用立即失败。</summary>
        public Task<UserPermissionProjection?> GetProjectionAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <summary>原权限先于验证/事务；未知键不能在此测试悄悄放行。</summary>
        public Task<bool> HasPermissionAsync(Guid userId, string permissionKey, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); Assert.AreEqual(SwiftExport.Contracts.DTOs.Auth.PermissionKeys.DemandPlanningMaterialSupplierSuggestionManage, permissionKey); return Task.FromResult(Allow); }
    }
}
