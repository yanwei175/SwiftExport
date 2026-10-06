using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Models.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.Auth.Permissions;
using SwiftExport.Application.Services.MasterData;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.MasterData.ExecutionEligibility;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Commands;
using SwiftExport.Core.Interfaces.MasterData;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Auditing;
using SwiftExport.Infrastructure.Repositories.Auth.Permissions;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.MasterData;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>主控串行验证资格服务、真实PG授权锁和审计；仅清理随机夹具，保留只追加日志及预置角色。</summary>
[TestClass]
[DoNotParallelize]
[TestCategory("PostgreSqlExecutionEligibilityIntegration")]
public sealed class PostgreSqlExecutionEligibilityServiceIntegrationTests
{
    /// <summary>三个对象仅变资格、版本和审计戳；实际日志和业务版本一起提交。</summary>
    [TestMethod]
    [DataRow(ExecutionEligibilityKind.Product)]
    [DataRow(ExecutionEligibilityKind.Customer)]
    [DataRow(ExecutionEligibilityKind.Supplier)]
    public async Task SuccessfulChange_CommitsStatusVersionAndAudit(ExecutionEligibilityKind kind)
    {
        await using var f = await Fixture.CreateAsync(kind);
        var result = await f.Service(f.First).SetStatusAsync(f.Command());
        Assert.IsTrue(result.Success);
        Assert.AreEqual(2, result.Data!.Version);
        Assert.IsFalse(result.Data.IsActive);
        await f.AssertTarget(2, false, 1);
        var log = await f.Second.Connection.QuerySingleAsync<BusinessOperationLog>(
            "SELECT command_id AS \"CommandId\",reason AS \"Reason\",changed_fields_json AS \"ChangedFieldsJson\" FROM public.business_operation_log WHERE aggregate_uid=@Uid", new { f.Target.Uid });
        Assert.AreNotEqual(Guid.Empty, log.CommandId);
        Assert.AreEqual("资格实库变更", log.Reason);
        StringAssert.Contains(log.ChangedFieldsJson!, "\"Before\":true");
        StringAssert.Contains(log.ChangedFieldsJson!, "\"After\":false");
        await Released(f.First);
    }

    /// <summary>同状态、旧版本及软删保持源业务拒绝，不能写版本或审计。</summary>
    [TestMethod]
    [DataRow(ServiceErrorCodes.StateBlocked)]
    [DataRow(ServiceErrorCodes.VersionConflict)]
    [DataRow(ServiceErrorCodes.NotFound)]
    public async Task OriginalRejection_DoesNotChangeFacts(string expected)
    {
        await using var f = await Fixture.CreateAsync();
        var command = f.Command();
        if (expected == ServiceErrorCodes.StateBlocked) command.IsActive = true;
        if (expected == ServiceErrorCodes.VersionConflict) command.ExpectedVersion = 2;
        if (expected == ServiceErrorCodes.NotFound) await f.Second.Connection.ExecuteAsync($"UPDATE public.{f.Table} SET is_deleted=TRUE WHERE uid=@Uid", new { f.Target.Uid });
        var result = await f.Service(f.First).SetStatusAsync(command);
        Assert.AreEqual(expected, result.Error?.Code);
        await f.AssertTarget(1, true, 0);
        await Released(f.First);
    }

    /// <summary>活动普通角色的精确资格权限允许修改，停用定义或软删关系均拒绝；不要求管理员角色。</summary>
    [TestMethod]
    [DataRow("valid")]
    [DataRow("inactive")]
    [DataRow("deletedRelation")]
    public async Task OrdinaryRole_UsesActualPermissionDefinition(string mode)
    {
        await using var f = await Fixture.CreateAsync(ordinary: true);
        if (mode == "inactive") await f.Second.Connection.ExecuteAsync("UPDATE public.sys_permission SET is_active=FALSE WHERE uid=@Uid", new { f.Permission.Uid });
        if (mode == "deletedRelation") await f.Second.Connection.ExecuteAsync("UPDATE public.sys_role_permission SET is_deleted=TRUE WHERE role_id=@Uid", new { f.Role.Uid });
        var result = await f.Service(f.First).SetStatusAsync(f.Command());
        if (mode == "valid") { Assert.IsTrue(result.Success); await f.AssertTarget(2, false, 1); }
        else { Assert.AreEqual(ServiceErrorCodes.Forbidden, result.Error?.Code); await f.AssertTarget(1, true, 0); }
        await Released(f.First);
    }

    /// <summary>管理员编码分支按源实现不要求IsSystem；本事务临时更改并回滚预置角色标记，不提交该变更。</summary>
    [TestMethod]
    public async Task AdministratorCode_DoesNotAddSystemFlagRequirement()
    {
        await using var f = await Fixture.CreateAsync();
        using var transaction = f.First.Connection.BeginTransaction(IsolationLevel.Serializable);
        f.First.Transaction = transaction;
        try
        {
            await f.First.Connection.ExecuteAsync("UPDATE public.sys_role SET is_system=FALSE WHERE uid=@Uid", new { Uid = f.AdminRole }, transaction);
            Assert.IsTrue(await new PostgreSqlExecutionEligibilityRepository(Context(f.First)).HasExecutionEligibilityPermissionAsync(f.Actor.Uid));
        }
        finally { transaction.Rollback(); f.First.Transaction = null; }
        Assert.IsTrue(await f.Second.Connection.ExecuteScalarAsync<bool>("SELECT is_system FROM public.sys_role WHERE uid=@Uid", new { Uid = f.AdminRole }));
    }

    /// <summary>真实JSON约束失败和日志SQL后取消原样抛出，资格、版本和审计都必须回滚。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AuditFailureAndCancellation_RollBackAtomically(bool cancelled)
    {
        await using var f = await Fixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        var logs = new ControlledLogs(Logs(f.First))
        {
            Before = log => { if (!cancelled) log.ChangedFieldsJson = "{invalid-json"; },
            After = () => { if (cancelled) cancellation.Cancel(); }
        };
        var change = f.Service(f.First, logs).SetStatusAsync(f.Command(), cancellation.Token);
        if (cancelled) await Assert.ThrowsAsync<OperationCanceledException>(() => change);
        else
        {
            // 当前列为text并通过实际CHECK校验JSON；不是jsonb输入转换错误，精确验证部署的约束身份。
            var failure = await Assert.ThrowsAsync<PostgresException>(() => change);
            Assert.AreEqual("23514", failure.SqlState);
            Assert.AreEqual("ck_business_operation_log_changed_json", failure.ConstraintName);
        }
        await f.AssertTarget(1, true, 0);
        await Released(f.First);
    }

    /// <summary>同目标两个资格命令排队后读到最新版本，第二方保持源VersionConflict而非40001。</summary>
    [TestMethod]
    public async Task CompetingEligibilityCommands_RecheckVersionAfterWait()
    {
        await using var f = await Fixture.CreateAsync();
        var logs = new ControlledLogs(Logs(f.First)) { Hold = true };
        var first = f.Service(f.First, logs).SetStatusAsync(f.Command());
        Task<SwiftExport.Contracts.Wrappers.ServiceResult<ExecutionEligibilityStatusResult>>? second = null;
        try { await logs.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); second = f.Service(f.Second).SetStatusAsync(f.Command()); await Blocked(f.Second); }
        finally { logs.Resume.TrySetResult(); await first; }
        Assert.IsTrue((await first).Success);
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, (await second!).Error?.Code);
        await f.AssertTarget(2, false, 1);
        await Released(f.First); await Released(f.Second);
    }

    /// <summary>真实RBAC先撤销操作者角色，等待中的资格命令重验最新授权返回Forbidden，不写目标。</summary>
    [TestMethod]
    public async Task RbacRevocation_RechecksAuthorizationAfterWait()
    {
        await using var f = await Fixture.CreateAsync();
        var logs = new ControlledLogs(Logs(f.Second)) { Hold = true };
        var rbac = new RbacManagementService(new(f.Second, NullLoggerFactory.Instance), new PostgreSqlRbacManagementRepository(Context(f.Second)),
            new UnusedEvaluation(), logs, new Identity(f.Other.Uid));
        var revoke = rbac.ReplaceUserRolesAsync(f.Actor.Uid, new ReplaceUserRolesRequest { ExpectedVersion = 1, RoleIds = [] }, default);
        Task<SwiftExport.Contracts.Wrappers.ServiceResult<ExecutionEligibilityStatusResult>>? change = null;
        try { await logs.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); change = f.Service(f.First).SetStatusAsync(f.Command()); await Blocked(f.First); }
        finally { logs.Resume.TrySetResult(); await revoke; }
        Assert.IsTrue((await revoke).Success);
        Assert.AreEqual(ServiceErrorCodes.Forbidden, (await change!).Error?.Code);
        await f.AssertTarget(1, true, 0);
        await Released(f.First); await Released(f.Second);
    }

    /// <summary>不同Kind先取得各自目标锁后真正等待同一角色目录，取消释放事务及审计租约。</summary>
    [TestMethod]
    public async Task CancellationDuringAuthorizationWait_ReleasesAllLocks()
    {
        await using var f = await Fixture.CreateAsync();
        await using var supplier = await Fixture.CreateAsync(ExecutionEligibilityKind.Supplier);
        using var cancellation = new CancellationTokenSource();
        var logs = new ControlledLogs(Logs(f.First)) { Hold = true };
        var first = f.Service(f.First, logs).SetStatusAsync(f.Command());
        try
        {
            await logs.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var second = supplier.Service(supplier.First).SetStatusAsync(supplier.Command(), cancellation.Token);
            await Blocked(supplier.First); cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => second);
        }
        finally { logs.Resume.TrySetResult(); await first; }
        Assert.IsTrue((await first).Success);
        await supplier.AssertTarget(1, true, 0);
        await Released(f.First); await Released(supplier.First);
    }

    /// <summary>普通CRUD先持写锁或行读锁；资格在首次快照前等待，提交后保留原VersionConflict，已有行读者能升级写锁。</summary>
    [TestMethod]
    [DataRow(ExecutionEligibilityKind.Product, false)]
    [DataRow(ExecutionEligibilityKind.Customer, false)]
    [DataRow(ExecutionEligibilityKind.Supplier, false)]
    [DataRow(ExecutionEligibilityKind.Product, true)]
    [DataRow(ExecutionEligibilityKind.Customer, true)]
    [DataRow(ExecutionEligibilityKind.Supplier, true)]
    public async Task OrdinaryCrudCompetition_PreservesOriginalVersionConflict(ExecutionEligibilityKind kind, bool rowReaderFirst)
    {
        await using var f = await Fixture.CreateAsync(kind);
        using var crud = f.Second.Connection.BeginTransaction();
        f.Second.Transaction = crud;
        var committed = false;
        Task<SwiftExport.Contracts.Wrappers.ServiceResult<ExecutionEligibilityStatusResult>>? change = null;
        try
        {
            if (rowReaderFirst)
                await f.Second.Connection.QuerySingleAsync<Guid>($"SELECT uid FROM public.{f.Table} WHERE uid=@Uid FOR UPDATE", new { f.Target.Uid }, crud);
            else
                await f.Second.Connection.ExecuteAsync($"UPDATE public.{f.Table} SET version=version+1 WHERE uid=@Uid", new { f.Target.Uid }, crud);
            change = f.Service(f.First).SetStatusAsync(f.Command());
            await Blocked(f.First);
            if (rowReaderFirst)
                await f.Second.Connection.ExecuteAsync($"UPDATE public.{f.Table} SET version=version+1 WHERE uid=@Uid", new { f.Target.Uid }, crud);
            crud.Commit(); committed = true;
        }
        finally
        {
            if (!committed) crud.Rollback(); f.Second.Transaction = null;
            // 已提交事务仍可能返回Connection；明确提交标记，并先收完资格任务再清理，避免夹具反向持锁。
            if (change is not null) await change;
        }
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, (await change!).Error?.Code);
        await f.AssertTarget(2, true, 0);
        await Released(f.First); await Released(f.Second);
    }

    /// <summary>等待前客户端版本恰为写入后最新版本时，继续原状态/删除判断，不能把所有竞争映射为版本冲突。</summary>
    [TestMethod]
    [DataRow("updated", null)]
    [DataRow("sameState", ServiceErrorCodes.StateBlocked)]
    [DataRow("deleted", ServiceErrorCodes.NotFound)]
    [DataRow("restored", null)]
    public async Task TargetWait_UsesLatestVersionAndVisibility(string mode, string? expected)
    {
        await using var f = await Fixture.CreateAsync();
        if (mode == "restored") await f.Second.Connection.ExecuteAsync("UPDATE public.product SET is_deleted=TRUE WHERE uid=@Uid", new { f.Target.Uid });
        using var crud = f.Second.Connection.BeginTransaction(); f.Second.Transaction = crud;
        var committed = false;
        Task<SwiftExport.Contracts.Wrappers.ServiceResult<ExecutionEligibilityStatusResult>>? change = null;
        try
        {
            await f.Second.Connection.ExecuteAsync("UPDATE public.product SET version=version+1,is_deleted=@Deleted,is_active=@Active WHERE uid=@Uid",
                new { f.Target.Uid, Deleted = mode == "deleted", Active = mode != "sameState" }, crud);
            var command = f.Command(); command.ExpectedVersion = 2;
            change = f.Service(f.First).SetStatusAsync(command); await Blocked(f.First); crud.Commit(); committed = true;
        }
        finally { if (!committed) crud.Rollback(); f.Second.Transaction = null; if (change is not null) await change; }
        var result = await change!;
        if (expected is null) { Assert.IsTrue(result.Success); await f.AssertTarget(3, false, 1); }
        else { Assert.AreEqual(expected, result.Error?.Code); await f.AssertTarget(2, mode != "sameState", 0); }
        await Released(f.First); await Released(f.Second);
    }

    /// <summary>资格首次快照前先取得目标锁时普通CRUD真实等待，普通SELECT仍允许；释放后CRUD完成且不丢失版本。</summary>
    [TestMethod]
    [DataRow(ExecutionEligibilityKind.Product)]
    [DataRow(ExecutionEligibilityKind.Customer)]
    [DataRow(ExecutionEligibilityKind.Supplier)]
    public async Task EligibilityFirst_BlocksCrudButAllowsOrdinaryRead(ExecutionEligibilityKind kind)
    {
        await using var f = await Fixture.CreateAsync(kind);
        var gate = new PermissionGate(new PostgreSqlExecutionEligibilityRepository(Context(f.First)));
        var change = f.Service(f.First, repository: gate).SetStatusAsync(f.Command());
        Task<int>? crud = null;
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var observer = Open();
            Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>($"SELECT version FROM public.{f.Table} WHERE uid=@Uid", new { f.Target.Uid }));
            crud = f.Second.Connection.ExecuteAsync($"UPDATE public.{f.Table} SET version=version+1 WHERE uid=@Uid", new { f.Target.Uid });
            await Blocked(f.Second);
        }
        finally { gate.Resume.TrySetResult(); await change; }
        Assert.IsTrue((await change).Success); Assert.AreEqual(1, await crud!);
        await f.AssertTarget(3, false, 1); await Released(f.First); await Released(f.Second);
    }

    /// <summary>EXCLUSIVE为单Kind表范围，另一个UID也等待；记录实际阻塞代价，不宣称行级并行。</summary>
    [TestMethod]
    public async Task TargetTableLock_AlsoBlocksOtherUidOfSameKind()
    {
        await using var f = await Fixture.CreateAsync(); await using var other = await Fixture.CreateAsync();
        var logs = new ControlledLogs(Logs(f.First)) { Hold = true };
        var change = f.Service(f.First, logs).SetStatusAsync(f.Command()); Task<int>? crud = null;
        try
        {
            await logs.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            crud = other.Second.Connection.ExecuteAsync("UPDATE public.product SET version=version+1 WHERE uid=@Uid", new { other.Target.Uid });
            await Blocked(other.Second);
        }
        finally { logs.Resume.TrySetResult(); await change; }
        Assert.IsTrue((await change).Success); Assert.AreEqual(1, await crud!);
        await f.AssertTarget(2, false, 1); await other.AssertTarget(2, true, 0);
        await Released(f.First); await Released(other.Second);
    }

    /// <summary>真实等待目标写入时取消，资格事务及准备租约全部释放，竞争方回滚且目标不变。</summary>
    [TestMethod]
    public async Task CancellationDuringTargetWait_ReleasesAllLocks()
    {
        await using var f = await Fixture.CreateAsync(); using var cancellation = new CancellationTokenSource();
        using var crud = f.Second.Connection.BeginTransaction(); f.Second.Transaction = crud;
        try
        {
            await f.Second.Connection.ExecuteAsync("UPDATE public.product SET version=version+1 WHERE uid=@Uid", new { f.Target.Uid }, crud);
            var change = f.Service(f.First).SetStatusAsync(f.Command(), cancellation.Token);
            await Blocked(f.First); cancellation.Cancel(); await Assert.ThrowsAsync<OperationCanceledException>(() => change);
            await Released(f.First);
        }
        finally { crud.Rollback(); f.Second.Transaction = null; }
        await f.AssertTarget(1, true, 0); await Released(f.Second);
    }

    /// <summary>三连接重放产品行锁→供应商写入，产品资格先等待目标不能占住授权目录，供应商资格释放后全链路完成。</summary>
    [TestMethod]
    public async Task TargetBeforeAuthorization_AvoidsReverseThreeConnectionCycle()
    {
        await using var product = await Fixture.CreateAsync(); await using var supplier = await Fixture.CreateAsync(ExecutionEligibilityKind.Supplier);
        using var crud = product.Second.Connection.BeginTransaction(); product.Second.Transaction = crud;
        var committed = false;
        var logs = new ControlledLogs(Logs(supplier.First)) { Hold = true };
        Task<SwiftExport.Contracts.Wrappers.ServiceResult<ExecutionEligibilityStatusResult>>? productChange = null;
        Task<SwiftExport.Contracts.Wrappers.ServiceResult<ExecutionEligibilityStatusResult>>? supplierChange = null;
        Task<int>? supplierWrite = null;
        try
        {
            await product.Second.Connection.QuerySingleAsync<Guid>("SELECT uid FROM public.product WHERE uid=@Uid FOR UPDATE", new { product.Target.Uid }, crud);
            productChange = product.Service(product.First).SetStatusAsync(product.Command()); await Blocked(product.First);
            supplierChange = supplier.Service(supplier.First, logs).SetStatusAsync(supplier.Command());
            await logs.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            supplierWrite = product.Second.Connection.ExecuteAsync("UPDATE public.supplier SET version=version+1 WHERE uid=@Uid", new { supplier.Target.Uid }, crud);
            await Blocked(product.Second); logs.Resume.TrySetResult();
            Assert.IsTrue((await supplierChange).Success); Assert.AreEqual(1, await supplierWrite);
            await product.Second.Connection.ExecuteAsync("UPDATE public.product SET version=version+1 WHERE uid=@Uid", new { product.Target.Uid }, crud);
            crud.Commit(); committed = true;
        }
        finally
        {
            logs.Resume.TrySetResult();
            if (supplierChange is not null) await supplierChange;
            if (supplierWrite is not null) await supplierWrite;
            if (!committed) crud.Rollback(); product.Second.Transaction = null;
            if (productChange is not null) await productChange;
        }
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, (await productChange!).Error?.Code);
        await product.AssertTarget(2, true, 0); await supplier.AssertTarget(3, false, 1);
        await Released(product.First); await Released(product.Second); await Released(supplier.First);
    }

    /// <summary>严格限定主控新独立库，拒绝默认运行、原库和备份，连接内容不得输出。</summary>
    private static DbSession Open()
    {
        var value = Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RBAC_TEST_CONNECTION");
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_PG_RBAC_TESTS") != "1" || string.IsNullOrEmpty(value))
            Assert.Inconclusive("未启用主控独立资格实库；本项未运行。");
        var settings = new NpgsqlConnectionStringBuilder(value);
        Assert.AreEqual("192.168.3.12", settings.Host); Assert.AreEqual(5432, settings.Port);
        Assert.AreEqual("dv_next_pg_rbac_tests_20261005", settings.Database);
        Assert.IsTrue(string.IsNullOrEmpty(settings.SearchPath) || settings.SearchPath == "public");
        settings.CommandTimeout = 15;
        return new(new PostgreSqlConnectionFactory(settings.ConnectionString));
    }

    /// <summary>仓储和日志使用同一请求连接、既有打戳及固定测试身份。</summary>
    private static RepositoryBaseContext Context(DbSession session)
    { var user = new Identity(null); return new(session, NullLoggerFactory.Instance, new AuditStampService(user), user); }
    /// <summary>用真实审计准备租约及日志SQL，不用模拟日志替代原子性。</summary>
    private static PostgreSqlBusinessOperationLogRepository Logs(DbSession session) => new(Context(session));

    /// <summary>通过独立观察连接确认实际阻塞，读取被测PID使用本地属性以免提前建立快照。</summary>
    private static async Task Blocked(DbSession session)
    {
        using var observer = Open(); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pid = ((NpgsqlConnection)session.Connection).ProcessID;
        while (!await observer.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT cardinality(pg_blocking_pids(@Pid))>0", new { Pid = pid }, cancellationToken: deadline.Token)))
            await Task.Delay(25, deadline.Token);
    }

    /// <summary>同时检查逻辑事务清空和真实会话锁归零。</summary>
    private static async Task Released(DbSession session)
    {
        Assert.IsNull(session.Transaction); using var observer = Open();
        Assert.AreEqual(0L, await observer.Connection.ExecuteScalarAsync<long>("SELECT count(*) FROM pg_locks WHERE pid=@Pid AND locktype IN ('advisory','relation','tuple')", new { Pid = ((NpgsqlConnection)session.Connection).ProcessID }));
    }

    /// <summary>随机夹具只在新库创建；记录自身UID清理，权限键和预置角色不得被全表清理。</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly DbSession First = Open(); public readonly DbSession Second = Open();
        public readonly SysUser Actor = new() { UserName = Prefix(), PasswordHash = "资格实库不可登录哈希" };
        public readonly SysUser Other = new() { UserName = Prefix(), PasswordHash = "资格实库不可登录哈希" };
        public readonly SysRole Role = new() { RoleCode = Prefix(), RoleName = "资格实库普通角色" };
        public readonly SysPermission Permission = new() { PermissionKey = "MasterData.Eligibility.Manage", DisplayName = "资格", GroupKey = "MasterData" };
        public required BaseEntity Target;
        public ExecutionEligibilityKind Kind;
        public Guid AdminRole;
        public string Table => Kind switch { ExecutionEligibilityKind.Product => "product", ExecutionEligibilityKind.Customer => "customer", _ => "supplier" };

        /// <summary>所有种子一事务提交以供第二连接观察，初始化失败仍精确清理自己的随机UID。</summary>
        public static async Task<Fixture> CreateAsync(ExecutionEligibilityKind kind = ExecutionEligibilityKind.Product, bool ordinary = false)
        {
            var target = kind switch
            {
                ExecutionEligibilityKind.Product => (BaseEntity)new Product { Sku = Prefix(), Unit = "PCS", SupplyType = "采购", MaterialType = "成品" },
                ExecutionEligibilityKind.Customer => new Customer { CustomerCode = Prefix(), CustomerName = "资格客户", DocRelateNo = Prefix() },
                _ => new Supplier { SupplierCode = Prefix(), SupplierName = "资格供应商", DocRelateNo = Prefix() }
            };
            var f = new Fixture { Kind = kind, Target = target };
            try
            {
                f.AdminRole = await f.First.Connection.QuerySingleAsync<Guid>("SELECT uid FROM public.sys_role WHERE role_code='SYSTEM_ADMIN' AND is_system AND is_active AND NOT is_deleted");
                using var transaction = f.First.Connection.BeginTransaction(); f.First.Transaction = transaction;
                try
                {
                    await f.Insert(f.Actor); await f.Insert(f.Other); await f.Insert(f.Role);
                    if (target is Product p) await f.Insert(p); else if (target is Customer c) await f.Insert(c); else await f.Insert((Supplier)target);
                    if (ordinary) { await f.Insert(f.Permission); await f.Insert(new SysRolePermission { RoleId = f.Role.Uid, PermissionId = f.Permission.Uid }); }
                    await f.Insert(new SysUserRole { UserId = f.Actor.Uid, RoleId = ordinary ? f.Role.Uid : f.AdminRole });
                    await f.Insert(new SysUserRole { UserId = f.Other.Uid, RoleId = f.AdminRole });
                    transaction.Commit();
                }
                finally { f.First.Transaction = null; }
                return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }

        /// <summary>默认请求与源服务一致；客户端不提供日志身份。</summary>
        public SetExecutionEligibilityStatusCommand Command() => new() { Kind = Kind, Uid = Target.Uid, ExpectedVersion = 1, IsActive = false, Reason = " 资格实库变更 " };
        /// <summary>组装真实服务及同会话PG仓储，装饰器仅控制竞态/失败时刻。</summary>
        public ExecutionEligibilityService Service(DbSession session, IBusinessOperationLogRepository? logs = null, IExecutionEligibilityRepository? repository = null)
            => new(new(session, NullLoggerFactory.Instance), repository ?? new PostgreSqlExecutionEligibilityRepository(Context(session)), logs ?? Logs(session), new Identity(Actor.Uid), new Correlation());
        /// <summary>用现有泛型PG仓储和实体元数据插入，不另创表或映射。</summary>
        private async Task Insert<T>(T row) where T : BaseEntity
            => Assert.IsTrue((await new PostgreSqlDapperRepository<T>(Context(First)).AddAsync(row)).Success);
        /// <summary>独立连接观察已提交或已回滚事实，不使用被测事务内部快照代替验收。</summary>
        public async Task AssertTarget(int version, bool active, long logs)
        {
            using var observer = Open();
            var row = await observer.Connection.QuerySingleAsync<ExecutionEligibilityStatusResult>($"SELECT version AS \"Version\",is_active AS \"IsActive\" FROM public.{Table} WHERE uid=@Uid", new { Target.Uid });
            Assert.AreEqual(version, row.Version); Assert.AreEqual(active, row.IsActive);
            Assert.AreEqual(logs, await observer.Connection.ExecuteScalarAsync<long>("SELECT count(*) FROM public.business_operation_log WHERE aggregate_uid=@Uid", new { Target.Uid }));
        }
        /// <summary>只删本次UID关系和实体；不可删除业务日志、内置角色或其他窗口数据。</summary>
        public async ValueTask DisposeAsync()
        {
            try
            {
                using var cleanup = Open(); using var transaction = cleanup.Connection.BeginTransaction();
                var ids = new[] { Actor.Uid, Other.Uid };
                await cleanup.Connection.ExecuteAsync("DELETE FROM public.sys_user_role WHERE user_id=ANY(@Ids)", new { Ids = ids }, transaction);
                await cleanup.Connection.ExecuteAsync("DELETE FROM public.sys_role_permission WHERE role_id=@Uid", new { Role.Uid }, transaction);
                await cleanup.Connection.ExecuteAsync("DELETE FROM public.sys_permission WHERE uid=@Uid", new { Permission.Uid }, transaction);
                await cleanup.Connection.ExecuteAsync("DELETE FROM public.sys_user WHERE uid=ANY(@Ids)", new { Ids = ids }, transaction);
                await cleanup.Connection.ExecuteAsync("DELETE FROM public.sys_role WHERE uid=@Uid AND NOT is_system", new { Role.Uid }, transaction);
                await cleanup.Connection.ExecuteAsync($"DELETE FROM public.{Table} WHERE uid=@Uid", new { Target.Uid }, transaction);
                transaction.Commit();
            }
            finally { First.Dispose(); Second.Dispose(); }
        }
        /// <summary>本批随机ASCII键避免与其他测试/业务唯一键冲突。</summary>
        private static string Prefix() => "PGET" + Guid.NewGuid().ToString("N").ToUpperInvariant();
    }

    /// <summary>保留真实日志准备，允许在实际日志前后注入失败或暂停以观察第二连接。</summary>
    private sealed class ControlledLogs(PostgreSqlBusinessOperationLogRepository inner) : IBusinessOperationLogRepository, ICommandTransactionPreparation
    {
        public Action<BusinessOperationLog>? Before; public Action? After; public bool Hold;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <inheritdoc />
        public Task<IAsyncDisposable?> AcquireBeforeTransactionAsync(Guid id, CancellationToken token) => inner.AcquireBeforeTransactionAsync(id, token);
        /// <summary>注入边界仍执行真正的日志SQL和同一业务事务。</summary>
        public async Task<int> InsertAsync(BusinessOperationLog log)
        { Before?.Invoke(log); var rows = await inner.InsertAsync(log); Entered.TrySetResult(); if (Hold) await Resume.Task; After?.Invoke(); return rows; }
        /// <inheritdoc />
        public Task<BusinessOperationLog?> GetByModuleCommandAsync(string module, Guid id) => inner.GetByModuleCommandAsync(module, id);
        /// <inheritdoc />
        public Task<BusinessOperationLog?> GetByCommandAsync(string module, string operation, Guid id) => inner.GetByCommandAsync(module, operation, id);
        /// <inheritdoc />
        public Task<IReadOnlyList<BusinessOperationTraceItemDto>> GetByAggregateAsync(string module, Guid uid, CancellationToken token = default) => inner.GetByAggregateAsync(module, uid, token);
    }
    /// <summary>显式转发首次快照前准备，在真实授权后暂停以观察普通CRUD阻塞；不得丢失强制准备契约。</summary>
    private sealed class PermissionGate(IExecutionEligibilityRepository inner) : IExecutionEligibilityRepository
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <inheritdoc />
        public Task PrepareTargetBeforeSnapshotAsync(ExecutionEligibilityKind kind, CancellationToken token = default) => inner.PrepareTargetBeforeSnapshotAsync(kind, token);
        /// <inheritdoc />
        public async Task<bool> HasExecutionEligibilityPermissionAsync(Guid id, CancellationToken token = default)
        { var result = await inner.HasExecutionEligibilityPermissionAsync(id, token); Entered.TrySetResult(); await Resume.Task; return result; }
        /// <inheritdoc />
        public Task<ExecutionEligibilityStatusResult?> GetForUpdateAsync(ExecutionEligibilityKind kind, Guid uid, CancellationToken token = default) => inner.GetForUpdateAsync(kind, uid, token);
        /// <inheritdoc />
        public Task<int> SetStatusAsync(SetExecutionEligibilityStatusCommand command, CancellationToken token = default) => inner.SetStatusAsync(command, token);
    }
    /// <summary>本轮随机已验证操作者，避免任何真实登录凭据。</summary>
    private sealed class Identity(Guid? id) : ICurrentUser
    { public Guid? UserId => id; public string UserName => "资格实库主控"; public bool IsAuthenticated => id.HasValue; }
    /// <summary>仅保留日志关联投影。</summary>
    private sealed class Correlation : ICorrelationContext
    { public string CorrelationId => "PG资格独立实库"; }
    /// <summary>RBAC写路径不得意外使用只读权限评估替代真实管理员锁。</summary>
    private sealed class UnusedEvaluation : IPermissionEvaluationService
    {
        /// <inheritdoc />
        public Task<UserPermissionProjection?> GetProjectionAsync(Guid uid, CancellationToken token = default) => throw new NotSupportedException();
        /// <inheritdoc />
        public Task<bool> HasPermissionAsync(Guid uid, string key, CancellationToken token = default) => throw new NotSupportedException();
    }
}
