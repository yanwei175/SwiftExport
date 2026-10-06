using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Models.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.Auth.Permissions;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Commands;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Auditing;
using SwiftExport.Infrastructure.Repositories.Auth.Permissions;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>在主控新建的独立 RBAC 库验证真实服务、仓储、Serializable 和审计原子性；只清理本次随机实体，保留只追加日志。</summary>
[TestClass]
[DoNotParallelize]
[TestCategory("PostgreSqlRbacServiceIntegration")]
public sealed class PostgreSqlRbacServiceIntegrationTests
{

    /// <summary>原角色授权与实际目录同步按两个先后次序交错；共享首锁保护新快照，目录只改展示字段，版本/关系/审计保持原合同。</summary>
    [TestMethod]
    public async Task RolePermissionsAndCatalogSync_KeepBothServiceOrdersConsistent()
    {
        Assert.AreEqual("M10-CATALOG-ROLE-20261006", Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RBAC_CATALOG_BATCH"));
        using var observer = Open();
        Assert.AreEqual(0L, await observer.Connection.ExecuteScalarAsync<long>("SELECT count(*) FROM public.sys_permission"),
            "只允许本批从空目录首次执行；完成的新目录不清空重跑。");
        await using var f = await Fixture.CreateAsync(false);
        using var sync = Open();
        var firstPid = ((NpgsqlConnection)f.First.Connection).ProcessID;
        var secondPid = ((NpgsqlConnection)f.Second.Connection).ProcessID;
        var syncPid = ((NpgsqlConnection)sync.Connection).ProcessID;
        Assert.AreNotEqual(firstPid, syncPid); Assert.AreNotEqual(secondPid, syncPid);
        var definitions = PermissionCatalog.All.Where(d => d.IsAssignable).Take(2).ToArray();
        Assert.HasCount(2, definitions);
        var permission = new SysPermission { PermissionKey = definitions[0].PermissionKey, DisplayName = "本批目录展示待同步",
            GroupKey = "M10-CATALOG", SortOrder = -101, IsActive = true };
        var logs = new ControlledLogs(Logs(f.First)) { Hold = true };
        var pending = new List<Task>();
        var waits = new List<string>();
        Guid secondPermissionId = Guid.Empty;
        try
        {
            // 仅新权限作为合法代码键夹具；实际工具负责完整112项目录，不造权限含义或绕过可分配性。
            var inserted = await new PostgreSqlDapperRepository<SysPermission>(Context(f.First)).AddAsync(permission);
            Assert.IsTrue(inserted.Success, inserted.ErrorCode + ": " + inserted.Message);
            var stableBefore = await observer.Connection.ExecuteScalarAsync<string>(
                "SELECT (to_jsonb(p)-'display_name'-'group_key'-'sort_order')::text FROM public.sys_permission p WHERE uid=@Id", new { Id = permission.Uid });
            var firstGrant = f.Service(f.First, logs).ReplaceRolePermissionsAsync(f.Role.Uid,
                new ReplaceRolePermissionsRequest { ExpectedVersion = 1, PermissionIds = [permission.Uid] }, default);
            pending.Add(firstGrant);
            await logs.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var firstSync = new SwiftExport.Tools.DbSeed.PostgreSqlPermissionCatalogSync(sync).RunAsync(false);
            pending.Add(firstSync);
            try
            {
                await WaitBlocked(sync);
                var actual = await observer.Connection.ExecuteScalarAsync<string>("""
                    SELECT json_build_object('stage','grant-first','waiting',a.pid,'blockers',pg_blocking_pids(a.pid),
                      'wait_type',a.wait_event_type,'wait_event',a.wait_event)::text
                    FROM pg_stat_activity a WHERE a.pid=@Sync AND @Owner=ANY(pg_blocking_pids(a.pid))
                      AND EXISTS(SELECT 1 FROM pg_locks WHERE pid=@Owner AND relation='public.sys_role'::regclass
                        AND mode='ShareRowExclusiveLock' AND granted)
                    """, new { Sync = syncPid, Owner = firstPid });
                Assert.IsNotNull(actual); waits.Add(actual);
                Assert.IsFalse(firstSync.IsCompleted);
            }
            finally { logs.Resume.TrySetResult(); }
            Assert.IsTrue((await firstGrant.WaitAsync(TimeSpan.FromSeconds(15))).Success);
            await firstSync.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.IsNull(sync.Transaction);
            Assert.AreEqual(PermissionCatalog.All.Count, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.sys_permission"));
            Assert.AreEqual(2, await observer.Connection.ExecuteScalarAsync<int>("SELECT version FROM public.sys_role WHERE uid=@Id", new { Id = f.Role.Uid }));
            Assert.AreEqual(permission.Uid, await observer.Connection.QuerySingleAsync<Guid>("SELECT permission_id FROM public.sys_role_permission WHERE role_id=@Id AND NOT is_deleted", new { Id = f.Role.Uid }));
            await CheckSeed();

            // 第二次工具先取得role锁，再因本例展示变更的permission锁等待；真实授权排在工具后，放行形成无环的真实三连接等待。
            secondPermissionId = await observer.Connection.QuerySingleAsync<Guid>("SELECT uid FROM public.sys_permission WHERE permission_key=@Key", new { Key = definitions[1].PermissionKey });
            using (var owner = f.First.Connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                f.First.Transaction = owner;
                Task? secondSync = null;
                Task<SwiftExport.Contracts.Wrappers.ServiceResult<RolePermissionsDto>>? secondGrant = null;
                var committed = false;
                try
                {
                    await f.First.Connection.ExecuteAsync("LOCK TABLE public.sys_permission IN SHARE ROW EXCLUSIVE MODE", transaction: owner);
                    Assert.AreEqual(1, await f.First.Connection.ExecuteAsync("UPDATE public.sys_permission SET display_name='本批同步先行展示',group_key='M10-CATALOG-SECOND',sort_order=-102 WHERE uid=@Id",
                        new { Id = permission.Uid }, owner));
                    secondSync = new SwiftExport.Tools.DbSeed.PostgreSqlPermissionCatalogSync(sync).RunAsync(false); pending.Add(secondSync);
                    await WaitBlocked(sync);
                    secondGrant = f.Service(f.Second).ReplaceRolePermissionsAsync(f.Role.Uid,
                        new ReplaceRolePermissionsRequest { ExpectedVersion = 2, PermissionIds = [secondPermissionId] }, default); pending.Add(secondGrant);
                    await WaitBlocked(f.Second);
                    var actual = await observer.Connection.ExecuteScalarAsync<string>("""
                        SELECT json_build_object('stage','sync-first','owner',@Owner,'sync',@Sync,'grant',@Grant,
                            'sync_blockers',pg_blocking_pids(@Sync),'grant_blockers',pg_blocking_pids(@Grant))::text
                        WHERE @Owner=ANY(pg_blocking_pids(@Sync)) AND @Sync=ANY(pg_blocking_pids(@Grant))
                          AND EXISTS(SELECT 1 FROM pg_locks WHERE pid=@Sync AND relation='public.sys_role'::regclass
                            AND mode='ShareRowExclusiveLock' AND granted)
                        """, new { Owner = firstPid, Sync = syncPid, Grant = secondPid });
                    Assert.IsNotNull(actual); waits.Add(actual);
                    Assert.IsFalse(secondSync.IsCompleted); Assert.IsFalse(secondGrant.IsCompleted);
                    owner.Commit(); committed = true; f.First.Transaction = null;
                    await secondSync.WaitAsync(TimeSpan.FromSeconds(15));
                    var assigned = await secondGrant.WaitAsync(TimeSpan.FromSeconds(15));
                    Assert.IsTrue(assigned.Success, assigned.Error?.Code + ": " + assigned.Message);
                }
                finally
                {
                    if (!committed) owner.Rollback();
                    f.First.Transaction = null;
                    if (secondSync is not null) await secondSync.WaitAsync(TimeSpan.FromSeconds(15));
                    if (secondGrant is not null) await secondGrant.WaitAsync(TimeSpan.FromSeconds(15));
                }
            }
            await CheckSeed();
            Assert.AreEqual(PermissionCatalog.All.Count, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.sys_permission"));
            Assert.AreEqual(3, await observer.Connection.ExecuteScalarAsync<int>("SELECT version FROM public.sys_role WHERE uid=@Id", new { Id = f.Role.Uid }));
            Assert.AreEqual(secondPermissionId, await observer.Connection.QuerySingleAsync<Guid>("SELECT permission_id FROM public.sys_role_permission WHERE role_id=@Id AND NOT is_deleted", new { Id = f.Role.Uid }));
            Assert.AreEqual(2L, await observer.Connection.ExecuteScalarAsync<long>("SELECT count(*) FROM public.business_operation_log WHERE aggregate_uid=@Id AND operation_type='ReplaceRolePermissions'", new { Id = f.Role.Uid }));
            await AssertReleased(f.First); await AssertReleased(f.Second); await AssertReleased(sync);
            Assert.IsNull(sync.Transaction);

            // 工具对本批已有定义严格只改三展示列；身份、稳定键、状态、版本和全部审计字段前后相同。
            async Task CheckSeed()
            {
                var current = await observer.Connection.QuerySingleAsync<SysPermission>("""
                    SELECT uid AS "Uid",permission_key AS "PermissionKey",display_name AS "DisplayName",
                        group_key AS "GroupKey",sort_order AS "SortOrder" FROM public.sys_permission WHERE uid=@Id
                    """, new { Id = permission.Uid });
                Assert.AreEqual(definitions[0].PermissionKey, current.PermissionKey);
                Assert.AreEqual(definitions[0].DisplayName, current.DisplayName);
                Assert.AreEqual(definitions[0].GroupKey, current.GroupKey);
                Assert.AreEqual(PermissionCatalog.All.ToList().FindIndex(d => d.PermissionKey == current.PermissionKey) + 1, current.SortOrder);
                Assert.AreEqual(stableBefore, await observer.Connection.ExecuteScalarAsync<string>(
                    "SELECT (to_jsonb(p)-'display_name'-'group_key'-'sort_order')::text FROM public.sys_permission p WHERE uid=@Id", new { Id = permission.Uid }));
            }
        }
        finally
        {
            logs.Resume.TrySetResult();
            await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(30));
            var auditIds = (await observer.Connection.QueryAsync<Guid>("SELECT uid FROM public.business_operation_log WHERE aggregate_uid=@Id", new { Id = f.Role.Uid })).ToArray();
            var permissionIds = (await observer.Connection.QueryAsync<Guid>("SELECT uid FROM public.sys_permission ORDER BY uid")).ToArray();
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Batch = "M10-CATALOG-ROLE-20261006",
                UserIds = new[] { f.Actor.Uid, f.Other.Uid, f.Target.Uid }, RoleId = f.Role.Uid, SeedPermissionId = permission.Uid,
                SecondPermissionId = secondPermissionId, CodePermissionCount = PermissionCatalog.All.Count, PermissionIds = permissionIds, AuditIds = auditIds,
                FirstPid = firstPid, SecondPid = secondPid, SyncPid = syncPid, Waits = waits,
                Policy = "原独立RBAC空目录首次；完整新代码目录及审计留存，本人可变用户/普通角色/关系精确清理，原96行全保护；非dv密码/HTTP" }));
        }
    }



    /// <summary>真实普通角色删除与用户分配按两种提交次序排队；等待后保留角色校验/仍有关联的原业务拒绝及审计原子性。</summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task RoleDeletionAndAssignment_RechecksAfterRealServiceWait(bool deleteFirst)
    {
        Assert.AreEqual("M10-ROLE-ASSIGNMENT-20261006", Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RBAC_ROLE_ASSIGNMENT_BATCH"),
            "仅主控登记的新场景执行，不无意混入旧全组。");
        await using var f = await Fixture.CreateAsync(false);
        var logs = new ControlledLogs(Logs(f.First)) { Hold = true };
        using var observer = Open();
        var firstPid = ((NpgsqlConnection)f.First.Connection).ProcessID;
        var secondPid = ((NpgsqlConnection)f.Second.Connection).ProcessID;
        Assert.AreNotEqual(firstPid, secondPid);
        Task<SwiftExport.Contracts.Wrappers.ServiceResult<bool>>? deletion = null;
        Task<SwiftExport.Contracts.Wrappers.ServiceResult<UserRolesDto>>? assignment = null;
        string? blocking = null;
        try
        {
            // 先行原服务在真实审计插入后、提交前暂停；后行服务继续原目录首锁及Serializable，屏障不制造新生产锁。
            if (deleteFirst)
            {
                deletion = f.Service(f.First, logs).DeleteRoleAsync(f.Role.Uid, 1, default);
                await logs.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                assignment = f.Service(f.Second).ReplaceUserRolesAsync(f.Target.Uid,
                    new ReplaceUserRolesRequest { ExpectedVersion = 1, RoleIds = [f.Role.Uid] }, default);
            }
            else
            {
                assignment = f.Service(f.First, logs).ReplaceUserRolesAsync(f.Target.Uid,
                    new ReplaceUserRolesRequest { ExpectedVersion = 1, RoleIds = [f.Role.Uid] }, default);
                await logs.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                deletion = f.Service(f.Second).DeleteRoleAsync(f.Role.Uid, 1, default);
            }
            await WaitBlocked(f.Second);
            blocking = await observer.Connection.ExecuteScalarAsync<string>("""
                SELECT json_build_object('first',@First,'second',a.pid,'blockers',pg_blocking_pids(a.pid),
                    'wait_type',a.wait_event_type,'wait_event',a.wait_event)::text
                FROM pg_stat_activity a WHERE a.pid=@Second AND @First=ANY(pg_blocking_pids(a.pid))
                  AND EXISTS(SELECT 1 FROM pg_locks l WHERE l.pid=@First AND l.relation='public.sys_role'::regclass
                    AND l.mode='ShareRowExclusiveLock' AND l.granted)
                """, new { First = firstPid, Second = secondPid });
            Assert.IsNotNull(blocking, "必须证明后行在先行真实目录锁等待，不能用延迟或旧快照代替服务返回验收。");
            logs.Resume.TrySetResult();
            var deleted = await deletion.WaitAsync(TimeSpan.FromSeconds(15));
            var assigned = await assignment.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.IsTrue(deleteFirst ? deleted.Success : assigned.Success);
            Assert.IsFalse(deleteFirst ? assigned.Success : deleted.Success);
            Assert.AreEqual(deleteFirst ? ServiceErrorCodes.Validation : ServiceErrorCodes.BusinessBlocked,
                deleteFirst ? assigned.Error?.Code : deleted.Error?.Code);
            Assert.AreEqual(deleteFirst ? 2 : 1, await observer.Connection.ExecuteScalarAsync<int>("SELECT version FROM public.sys_role WHERE uid=@Id", new { Id = f.Role.Uid }));
            Assert.AreEqual(deleteFirst, await observer.Connection.ExecuteScalarAsync<bool>("SELECT is_deleted FROM public.sys_role WHERE uid=@Id", new { Id = f.Role.Uid }));
            Assert.AreEqual(deleteFirst ? 1 : 2, await observer.Connection.ExecuteScalarAsync<int>("SELECT version FROM public.sys_user WHERE uid=@Id", new { Id = f.Target.Uid }));
            Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT credential_version FROM public.sys_user WHERE uid=@Id", new { Id = f.Target.Uid }));
            Assert.AreEqual(deleteFirst ? 0L : 1L, await observer.Connection.ExecuteScalarAsync<long>("SELECT count(*) FROM public.sys_user_role WHERE user_id=@User AND role_id=@Role AND NOT is_deleted", new { User = f.Target.Uid, Role = f.Role.Uid }));
            var operation = await observer.Connection.QuerySingleAsync<string>("SELECT operation_type FROM public.business_operation_log WHERE aggregate_uid=ANY(@Ids)", new { Ids = new[] { f.Role.Uid, f.Target.Uid } });
            Assert.AreEqual(deleteFirst ? "DeleteRole" : "ReplaceUserRoles", operation);
            await AssertReleased(f.First); await AssertReleased(f.Second);
        }
        finally
        {
            logs.Resume.TrySetResult();
            // 先等待所有已启动服务终态，再让原Fixture只清本人UID可变实体；历史日志及本次成功审计留存。
            await Task.WhenAll(new Task?[] { deletion, assignment }.Where(task => task is not null).Cast<Task>()).WaitAsync(TimeSpan.FromSeconds(25));
            var auditIds = (await observer.Connection.QueryAsync<Guid>("SELECT uid FROM public.business_operation_log WHERE aggregate_uid=ANY(@Ids)", new { Ids = new[] { f.Role.Uid, f.Target.Uid } })).ToArray();
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Batch = "M10-ROLE-ASSIGNMENT-20261006", DeleteFirst = deleteFirst,
                UserIds = new[] { f.Actor.Uid, f.Other.Uid, f.Target.Uid }, RoleId = f.Role.Uid, AuditIds = auditIds,
                FirstPid = firstPid, SecondPid = secondPid, Blocking = blocking,
                Policy = "仅新独立RBAC库/真实服务；复用原随机不可登录管理员夹具，非dv密码登录；本人可变夹具按精确UID清理，新审计与旧事实留存" }));
        }
    }


    /// <summary>普通用户整组授权成功时聚合和关系与唯一审计一起提交；凭据版本不受影响。</summary>
    [TestMethod]
    public async Task UserRoles_CommitsAggregateRelationsAndAuditTogether()
    {
        await using var f = await Fixture.CreateAsync(false);
        var result = await f.Service(f.First).ReplaceUserRolesAsync(f.Target.Uid,
            new ReplaceUserRolesRequest { ExpectedVersion = 1, RoleIds = [f.Role.Uid] }, default);
        Assert.IsTrue(result.Success);
        using var observer = Open();
        Assert.AreEqual(2, await observer.Connection.ExecuteScalarAsync<int>(
            "SELECT version FROM public.sys_user WHERE uid=@Uid", new { f.Target.Uid }));
        Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>(
            "SELECT credential_version FROM public.sys_user WHERE uid=@Uid", new { f.Target.Uid }));
        Assert.AreEqual(f.Role.Uid, await observer.Connection.QuerySingleAsync<Guid>(
            "SELECT role_id FROM public.sys_user_role WHERE user_id=@Uid AND NOT is_deleted", new { f.Target.Uid }));
        var log = await observer.Connection.QuerySingleAsync<BusinessOperationLog>(
            "SELECT command_id AS \"CommandId\", changed_fields_json AS \"ChangedFieldsJson\" FROM public.business_operation_log WHERE aggregate_uid=@Uid",
            new { f.Target.Uid });
        Assert.AreNotEqual(Guid.Empty, log.CommandId);
        StringAssert.Contains(log.ChangedFieldsJson!, f.Role.Uid.ToString());
        await AssertReleased(f.First);
    }

    /// <summary>真实 JSON 约束失败或审计 SQL 后取消都使先前关系和版本回滚，审计也不能部分提交。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AuditFailureOrCancellation_RollsBackAllFacts(bool cancelled)
    {
        await using var f = await Fixture.CreateAsync(false);
        using var cancellation = new CancellationTokenSource();
        var logs = new ControlledLogs(Logs(f.First))
        {
            BeforeInsert = log => { if (!cancelled) log.ChangedFieldsJson = "{invalid-json"; },
            AfterInsert = () => { if (cancelled) { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); } }
        };
        var write = f.Service(f.First, logs).ReplaceUserRolesAsync(f.Target.Uid,
            new ReplaceUserRolesRequest { ExpectedVersion = 1, RoleIds = [f.Role.Uid] }, cancellation.Token);
        if (cancelled) await Assert.ThrowsAsync<OperationCanceledException>(() => write);
        else Assert.AreEqual(ServiceErrorCodes.Unexpected, (await write).Error?.Code);
        using var observer = Open();
        Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>(
            "SELECT version FROM public.sys_user WHERE uid=@Uid", new { f.Target.Uid }));
        Assert.AreEqual(0L, await observer.Connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM public.sys_user_role WHERE user_id=@Uid", new { f.Target.Uid }));
        Assert.AreEqual(0L, await observer.Connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM public.business_operation_log WHERE aggregate_uid=@Uid", new { f.Target.Uid }));
        await AssertReleased(f.First);
    }

    /// <summary>只有一名有效管理员时，停用本人及清空本人角色均保持原 BusinessBlocked，不写审计或版本。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LastAdministrator_IsPreserved(bool disable)
    {
        await using var f = await Fixture.CreateAsync(false);
        var service = f.Service(f.First);
        var code = disable
            ? (await service.SetUserStatusAsync(f.Actor.Uid, new SetManagedStatusRequest { ExpectedVersion = 1, IsActive = false }, default)).Error?.Code
            : (await service.ReplaceUserRolesAsync(f.Actor.Uid, new ReplaceUserRolesRequest { ExpectedVersion = 1, RoleIds = [] }, default)).Error?.Code;
        Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, code);
        Assert.AreEqual(1L, await ActiveAdministrators());
        Assert.AreEqual(1, (await Repository(f.First).GetUserAsync(f.Actor.Uid, default))!.Version);
        Assert.AreEqual(0L, await f.First.Connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM public.business_operation_log WHERE aggregate_uid=@Uid", new { f.Actor.Uid }));
        await AssertReleased(f.First);
    }

    /// <summary>两管理员排队撤销自身角色后第二方读取最新管理员数并返回原 BusinessBlocked，不能把旧快照40001当业务结果。</summary>
    [TestMethod]
    public async Task ConcurrentOwnRevocation_PreservesLastAdministratorAndOriginalRejection()
    {
        await RunConcurrentRevocation(false);
    }

    /// <summary>两管理员互撤时第二操作者在等待后读取最新身份，返回原 Forbidden；不改变错误契约或自动重试。</summary>
    [TestMethod]
    public async Task ConcurrentMutualRevocation_RechecksActorAfterWait()
    {
        await RunConcurrentRevocation(true);
    }

    /// <summary>管理员锁真实等待期间取消请求，回滚第二事务并释放租约，不影响第一命令提交。</summary>
    [TestMethod]
    public async Task CancellationWhileWaiting_ReleasesSecondTransactionAndLease()
    {
        await using var f = await Fixture.CreateAsync(true);
        using var cancellation = new CancellationTokenSource();
        var logs = new ControlledLogs(Logs(f.First)) { Hold = true };
        var first = f.Service(f.First, logs).ReplaceUserRolesAsync(f.Actor.Uid, EmptyRoles(), default);
        Task<SwiftExport.Contracts.Wrappers.ServiceResult<UserRolesDto>>? second = null;
        try
        {
            await logs.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            second = f.Service(f.Second, actor: f.Other.Uid).ReplaceUserRolesAsync(f.Other.Uid, EmptyRoles(), cancellation.Token);
            await WaitBlocked(f.Second);
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => second);
        }
        finally { logs.Resume.TrySetResult(); await first; }
        Assert.IsTrue((await first).Success);
        Assert.AreEqual(1L, await ActiveAdministrators());
        await AssertReleased(f.First);
        await AssertReleased(f.Second);
    }

    /// <summary>由真实审计边界挂起第一事务，观察第二连接实际阻塞，再提交第一方；两种竞争保留源错误码。</summary>
    private static async Task RunConcurrentRevocation(bool mutual)
    {
        await using var f = await Fixture.CreateAsync(true);
        var logs = new ControlledLogs(Logs(f.First)) { Hold = true };
        var firstTarget = mutual ? f.Other.Uid : f.Actor.Uid;
        var secondTarget = mutual ? f.Actor.Uid : f.Other.Uid;
        var first = f.Service(f.First, logs).ReplaceUserRolesAsync(firstTarget, EmptyRoles(), default);
        Task<SwiftExport.Contracts.Wrappers.ServiceResult<UserRolesDto>>? second = null;
        try
        {
            await logs.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            second = f.Service(f.Second, actor: f.Other.Uid).ReplaceUserRolesAsync(secondTarget, EmptyRoles(), default);
            await WaitBlocked(f.Second);
        }
        finally { logs.Resume.TrySetResult(); await first; }
        Assert.IsTrue((await first).Success);
        Assert.IsNotNull(second);
        var rejected = await second.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.AreEqual(mutual ? ServiceErrorCodes.Forbidden : ServiceErrorCodes.BusinessBlocked, rejected.Error?.Code,
            "PG 锁等待后须按源库最新身份/管理员数量拒绝；Unexpected 不能充当已验证的业务契约。");
        Assert.AreEqual(1L, await ActiveAdministrators());
        using var observer = Open();
        Assert.AreEqual(1L, await observer.Connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM public.business_operation_log WHERE aggregate_uid=ANY(@Ids)",
            new { Ids = new[] { f.Actor.Uid, f.Other.Uid } }));
        await AssertReleased(f.First);
        await AssertReleased(f.Second);
    }

    /// <summary>空集合表示撤销全部角色，保持来源请求版本为1。</summary>
    private static ReplaceUserRolesRequest EmptyRoles() => new() { ExpectedVersion = 1, RoleIds = [] };

    /// <summary>只从显式启用的新独立测试库环境变量读取连接，拒绝dv_next、原故障库及备份。</summary>
    private static DbSession Open()
    {
        var connection = Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RBAC_TEST_CONNECTION");
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_PG_RBAC_TESTS") != "1" || string.IsNullOrEmpty(connection))
            Assert.Inconclusive("未启用主控独立 RBAC 测试库；本项未运行。");
        NpgsqlConnectionStringBuilder settings;
        try { settings = new(connection); }
        catch (ArgumentException) { Assert.Fail("RBAC 独立测试连接格式无效。"); throw; }
        Assert.AreEqual("192.168.3.12", settings.Host);
        Assert.AreEqual(5432, settings.Port);
        Assert.AreEqual("dv_next_pg_rbac_tests_20261005", settings.Database);
        Assert.IsTrue(string.IsNullOrEmpty(settings.SearchPath) || settings.SearchPath == "public");
        settings.CommandTimeout = 15;
        return new(new PostgreSqlConnectionFactory(settings.ConnectionString));
    }

    /// <summary>只组装当前PG仓储及同scope审计上下文。</summary>
    private static RepositoryBaseContext Context(DbSession session)
    {
        var user = new Identity(null);
        return new(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
    }
    /// <summary>构造真实 RBAC 仓储，不改变事务边界。</summary>
    private static PostgreSqlRbacManagementRepository Repository(DbSession session) => new(Context(session));
    /// <summary>构造真实审计仓储，同scope准备和写入共用一条物理连接。</summary>
    private static PostgreSqlBusinessOperationLogRepository Logs(DbSession session) => new(Context(session));

    /// <summary>通过独立观察连接查询全库有效管理员，测试库不存在历史管理员干扰。</summary>
    private static async Task<long> ActiveAdministrators()
    {
        using var observer = Open();
        return await observer.Connection.ExecuteScalarAsync<long>("""
            SELECT count(DISTINCT u.uid) FROM public.sys_user u
            JOIN public.sys_user_role ur ON ur.user_id=u.uid AND NOT ur.is_deleted
            JOIN public.sys_role r ON r.uid=ur.role_id AND NOT r.is_deleted AND r.is_active
            WHERE NOT u.is_deleted AND u.is_active AND r.role_code='SYSTEM_ADMIN'
            """);
    }

    /// <summary>不先查询被测连接以免建立其事务快照，物理PID由Npgsql本地属性读取。</summary>
    private static async Task WaitBlocked(DbSession waiting)
    {
        using var observer = Open();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pid = ((NpgsqlConnection)waiting.Connection).ProcessID;
        while (!await observer.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT cardinality(pg_blocking_pids(@Pid))>0", new { Pid = pid }, cancellationToken: deadline.Token)))
            await Task.Delay(25, deadline.Token);
    }

    /// <summary>验证真实数据库锁与逻辑事务均已释放，不用假件状态代替实库清理。</summary>
    private static async Task AssertReleased(DbSession session)
    {
        Assert.IsNull(session.Transaction);
        using var observer = Open();
        Assert.AreEqual(0L, await observer.Connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM pg_locks WHERE locktype='advisory' AND pid=@Pid",
            new { Pid = ((NpgsqlConnection)session.Connection).ProcessID }));
    }

    /// <summary>随机账号和普通角色夹具；只借用新测试库预置的内置角色，清理仅删除自身UID关系及实体。</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly DbSession First = Open();
        public readonly DbSession Second = Open();
        public readonly SysUser Actor = NewUser();
        public readonly SysUser Other = NewUser();
        public readonly SysUser Target = NewUser();
        public readonly SysRole Role = new() { RoleCode = "R" + Guid.NewGuid().ToString("N").ToUpperInvariant(), RoleName = "独立服务测试普通角色" };

        /// <summary>确认没有其他测试管理员，再在独立库插入本次夹具；不初始化或清理原故障库。</summary>
        public static async Task<Fixture> CreateAsync(bool twoAdministrators)
        {
            var f = new Fixture();
            try
            {
                Assert.AreEqual(0L, await ActiveAdministrators(), "存在其他管理员夹具，拒绝混合或重置测试数据。");
                var adminRole = await f.First.Connection.QuerySingleAsync<Guid>(
                    "SELECT uid FROM public.sys_role WHERE role_code='SYSTEM_ADMIN' AND is_system AND is_active AND NOT is_deleted");
                using var transaction = f.First.Connection.BeginTransaction();
                f.First.Transaction = transaction;
                try
                {
                    await f.Insert(f.Actor); await f.Insert(f.Other); await f.Insert(f.Target); await f.Insert(f.Role);
                    await f.Insert(new SysUserRole { UserId = f.Actor.Uid, RoleId = adminRole });
                    if (twoAdministrators) await f.Insert(new SysUserRole { UserId = f.Other.Uid, RoleId = adminRole });
                    transaction.Commit();
                }
                finally { f.First.Transaction = null; }
                return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }

        /// <summary>真实服务只用PG仓储，审计装饰器仅控制故障或并发同步边界。</summary>
        public RbacManagementService Service(DbSession session, IBusinessOperationLogRepository? logs = null, Guid? actor = null) =>
            new(new ServiceBaseContext(session, NullLoggerFactory.Instance), Repository(session), new ReadOnlyEvaluation(),
                logs ?? Logs(session), new Identity(actor ?? Actor.Uid));

        /// <summary>按现有实体元数据和参数类型插入夹具，不引入业务实体或字段。</summary>
        private async Task Insert<T>(T entity) where T : BaseEntity
        {
            var context = Context(First); context.AuditStamp.StampForInsert(entity);
            var metadata = PostgreSqlEntityMetadata.GetMetadata(typeof(T));
            var columns = string.Join(",", metadata.Columns.Select(c => PostgreSqlNames.Quote(c.ColumnName)));
            var parameters = string.Join(",", metadata.Columns.Select(c => "@" + c.ParameterName));
            await First.Connection.ExecuteAsync(new CommandDefinition(
                $"INSERT INTO public.{PostgreSqlNames.Quote(metadata.TableName)} ({columns}) VALUES ({parameters})",
                PostgreSqlParameters.Entity(entity, metadata), First.Transaction));
        }

        /// <summary>仅精确清理随机UID；审计保留只追加性并留在新专库供验收追溯，内置角色不修改。</summary>
        public async ValueTask DisposeAsync()
        {
            try
            {
                using var cleanup = Open();
                var ids = new[] { Actor.Uid, Other.Uid, Target.Uid };
                using var transaction = cleanup.Connection.BeginTransaction();
                await cleanup.Connection.ExecuteAsync("DELETE FROM public.sys_user_role WHERE user_id=ANY(@Ids)", new { Ids = ids }, transaction);
                await cleanup.Connection.ExecuteAsync("DELETE FROM public.sys_role_permission WHERE role_id=@Uid", new { Role.Uid }, transaction);
                await cleanup.Connection.ExecuteAsync("DELETE FROM public.sys_user WHERE uid=ANY(@Ids)", new { Ids = ids }, transaction);
                await cleanup.Connection.ExecuteAsync("DELETE FROM public.sys_role WHERE uid=@Uid AND NOT is_system", new { Role.Uid }, transaction);
                transaction.Commit();
            }
            finally { First.Dispose(); Second.Dispose(); }
        }

        /// <summary>预生成仅用于本次测试的有效账号，不提供可登录密码。</summary>
        private static SysUser NewUser() => new() { UserName = "RBAC_" + Guid.NewGuid().ToString("N"), PasswordHash = "独立实库不可登录哈希" };
    }

    /// <summary>复用真实审计准备与SQL，仅为测试在审计边界暂停或注入取消/无效JSON。</summary>
    private sealed class ControlledLogs(PostgreSqlBusinessOperationLogRepository inner) : IBusinessOperationLogRepository, ICommandTransactionPreparation
    {
        public bool Hold;
        public Action<BusinessOperationLog>? BeforeInsert;
        public Action? AfterInsert;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>准备能力必须保留真实租约，不能通过装饰器失去高隔离协议。</summary>
        public Task<IAsyncDisposable?> AcquireBeforeTransactionAsync(Guid commandId, CancellationToken token) => inner.AcquireBeforeTransactionAsync(commandId, token);
        /// <summary>真实SQL前后注入测试行为，暂停不提交事务。</summary>
        public async Task<int> InsertAsync(BusinessOperationLog log)
        {
            BeforeInsert?.Invoke(log);
            var rows = await inner.InsertAsync(log);
            Entered.TrySetResult();
            if (Hold) await Resume.Task;
            AfterInsert?.Invoke();
            return rows;
        }
        /// <inheritdoc />
        public Task<BusinessOperationLog?> GetByModuleCommandAsync(string module, Guid commandId) => inner.GetByModuleCommandAsync(module, commandId);
        /// <inheritdoc />
        public Task<BusinessOperationLog?> GetByCommandAsync(string module, string operationType, Guid commandId) => inner.GetByCommandAsync(module, operationType, commandId);
        /// <inheritdoc />
        public Task<IReadOnlyList<BusinessOperationTraceItemDto>> GetByAggregateAsync(string module, Guid aggregateUid, CancellationToken cancellationToken = default) => inner.GetByAggregateAsync(module, aggregateUid, cancellationToken);
    }

    /// <summary>测试身份来自随机管理员关系，只有服务写路径使用。</summary>
    private sealed class Identity(Guid? id) : ICurrentUser
    {
        public Guid? UserId => id;
        public string UserName => "RBAC独立实库主控";
        public bool IsAuthenticated => id.HasValue;
    }

    /// <summary>被测写路径不得意外改成权限只读投影，出现新调用即失败。</summary>
    private sealed class ReadOnlyEvaluation : IPermissionEvaluationService
    {
        /// <inheritdoc />
        public Task<UserPermissionProjection?> GetProjectionAsync(Guid userId, CancellationToken token = default) => throw new NotSupportedException();
        /// <inheritdoc />
        public Task<bool> HasPermissionAsync(Guid userId, string key, CancellationToken token = default) => throw new NotSupportedException();
    }
}
