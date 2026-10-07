using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Tools.AdminPasswordRecovery;

namespace SwiftExport.Application.Tests.Database;

/// <summary>仅主控执行的原生恢复首锁竞争，验证首次快照、等待取消与物理连接释放。</summary>
[TestClass, DoNotParallelize]
public sealed class PostgreSqlAdminPasswordRecoveryRolePreparationTests
{
    /// <summary>登记库、随机身份、既有角色及提交/保留策略，不输出连接或哈希。</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>候选在首角色SRE排队后读取owner新提交管理员；保留随机User/UR并验证仅五字段变更。</summary>
    [TestMethod]
    public async Task RoleWait_FirstSnapshotSeesCommittedNewAdministrator()
    {
        var connection = Connection(); var uid = Guid.NewGuid(); var linkUid = Guid.NewGuid();
        var name = "P06ROLE" + uid.ToString("N"); var application = "P06ROLE-" + Guid.NewGuid().ToString("N");
        Register(connection, uid, linkUid, null, "owner提交后User/UR保留，未知提交不清理");
        using var owner = Open(connection); using var observer = Open(connection);
        using var transaction = owner.Connection.BeginTransaction(IsolationLevel.ReadCommitted); owner.Transaction = transaction;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task<AdminPasswordRecoveryStatus>? pending = null; var commitAttempted = false; var committed = false;
        try
        {
            await RoleLock(owner, deadline.Token);
            var role = await ExistingRole(owner, deadline.Token);
            Register(connection, uid, linkUid, role.Uid, "初始化前：既有角色只读；随机User/UR成功保留");
            var user = new SysUser
            {
                Uid = uid, Version = 7, CredentialVersion = 3, UserName = name, NickName = "首锁夹具",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N")), IsActive = true,
                Email = "role-fixture@example.invalid", PhoneNumber = "fixture", CreatedBy = "首锁夹具", UpdatedBy = "首锁夹具",
                CreatedDate = new DateTime(2026, 10, 5), UpdatedDate = new DateTime(2026, 10, 5)
            };
            var link = new SysUserRole { Uid = linkUid, Version = 2, UserId = uid, RoleId = role.Uid, CreatedBy = "首锁夹具" };
            await Insert(owner, user, deadline.Token); await Insert(owner, link, deadline.Token);
            var before = await Read<SysUser>(owner, uid); var linkBefore = await Read<SysUserRole>(owner, linkUid);
            var start = await observer.Connection.ExecuteScalarAsync<DateTime>("SELECT clock_timestamp()::timestamp");
            var replacement = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"));
            pending = new PostgreSqlAdminPasswordRecoveryStore(CandidateConnection(connection, application))
                .ResetExistingAdminAsync(name, replacement, deadline.Token);
            var candidatePid = await ObserveRoleWait(observer, owner, application, pending, deadline.Token);
            Assert.AreNotEqual(Pid(owner), candidatePid);
            Assert.AreEqual(0, await Count<SysUser>(observer, uid)); Assert.AreEqual(0, await Count<SysUserRole>(observer, linkUid));
            commitAttempted = true;
            try { transaction.Commit(); committed = true; }
            catch { Register(connection, uid, linkUid, role.Uid, "owner提交结果未知：仅清单核验，不删除/重试"); throw; }
            finally { owner.Transaction = null; }
            Assert.AreEqual(AdminPasswordRecoveryStatus.Updated, await pending);
            await WaitDisconnected(observer, application, candidatePid, deadline.Token);
            var after = await Read<SysUser>(observer, uid);
            Assert.IsTrue(after.PasswordHash == replacement, "恢复后凭据应为本例替换值，不输出哈希。");
            Assert.IsTrue(after.PasswordHash != before.PasswordHash, "恢复应改变本例随机凭据。");
            Assert.AreEqual(before.Version + 1, after.Version); Assert.AreEqual(before.CredentialVersion + 1, after.CredentialVersion);
            Assert.AreEqual("LocalAdminPasswordRecovery", after.UpdatedBy); Assert.IsNotNull(after.UpdatedDate);
            var end = await observer.Connection.ExecuteScalarAsync<DateTime>("SELECT clock_timestamp()::timestamp");
            Assert.IsTrue(after.UpdatedDate >= start && after.UpdatedDate <= end, "保留数据库墙上时间打戳，不打印凭据。");
            Compare(before, after, ["PasswordHash", "Version", "CredentialVersion", "UpdatedDate", "UpdatedBy"]);
            Compare(linkBefore, await Read<SysUserRole>(observer, linkUid), []);
            Compare(role, await Read<SysRole>(observer, role.Uid), []);
            Assert.AreEqual(1, await Count<SysUser>(observer, uid)); Assert.AreEqual(1, await Count<SysUserRole>(observer, linkUid));
            Register(connection, uid, linkUid, role.Uid, "成功：User+1/UR+1留存，其他48表由主控摘要核验");
        }
        finally
        {
            deadline.Cancel();
            // 先归还owner表锁再收候选，失败时也不能靠候选取消是否及时决定owner能否释放。
            try
            {
                if (!commitAttempted) transaction.Rollback();
            }
            finally
            {
                try { transaction.Dispose(); }
                finally
                {
                    owner.Transaction = null;
                    if (pending is not null) { try { await pending; } catch (OperationCanceledException) when (deadline.IsCancellationRequested) { } }
                }
            }
            if (!commitAttempted)
            {
                Assert.AreEqual(0, await Count<SysUser>(observer, uid)); Assert.AreEqual(0, await Count<SysUserRole>(observer, linkUid));
            }
            else if (!committed) Register(connection, uid, linkUid, null, "提交未知：保留初始化清单交主控");
        }
    }

    /// <summary>首角色表锁等待取消向上传播；候选物理连接终止后第三backend有界重取，场景没有账号DML。</summary>
    [TestMethod]
    public async Task CancelledRoleWait_DisconnectsAndAllowsThirdBackend()
    {
        var connection = Connection(); var uid = Guid.NewGuid(); var application = "P06ROLE-" + Guid.NewGuid().ToString("N");
        Register(connection, uid, null, null, "取消场景零账号/关系DML，仅随机不存在用户名");
        using var owner = Open(connection); using var observer = Open(connection);
        using var transaction = owner.Connection.BeginTransaction(IsolationLevel.ReadCommitted); owner.Transaction = transaction;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); using var cancel = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        Task<AdminPasswordRecoveryStatus>? pending = null; var rolledBack = false;
        try
        {
            await RoleLock(owner, deadline.Token); var role = await ExistingRole(owner, deadline.Token);
            Register(connection, uid, null, role.Uid, "取消场景：既有角色只读，无初始化写入");
            var replacement = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"));
            pending = new PostgreSqlAdminPasswordRecoveryStore(CandidateConnection(connection, application))
                .ResetExistingAdminAsync("P06ROLE" + uid.ToString("N"), replacement, cancel.Token);
            var candidatePid = await ObserveRoleWait(observer, owner, application, pending, deadline.Token);
            cancel.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
            await WaitDisconnected(observer, application, candidatePid, deadline.Token);
            transaction.Rollback(); rolledBack = true; owner.Transaction = null;
            using var third = Open(connection); using var thirdTransaction = third.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
            third.Transaction = thirdTransaction;
            try
            {
                Assert.AreNotEqual(Pid(owner), Pid(third)); Assert.AreNotEqual(candidatePid, Pid(third));
                await third.Connection.ExecuteAsync(new CommandDefinition("SET LOCAL lock_timeout = '3s';", transaction: thirdTransaction, cancellationToken: deadline.Token));
                await RoleLock(third, deadline.Token); thirdTransaction.Rollback();
            }
            finally { third.Transaction = null; }
            Compare(role, await Read<SysRole>(observer, role.Uid), []);
            Assert.AreEqual(0, await Count<SysUser>(observer, uid));
        }
        finally
        {
            cancel.Cancel();
            try { if (!rolledBack) transaction.Rollback(); }
            finally
            {
                try { transaction.Dispose(); }
                finally
                {
                    owner.Transaction = null;
                    if (pending is not null) { try { await pending; } catch (OperationCanceledException) when (cancel.IsCancellationRequested) { } }
                }
            }
        }
    }

    /// <summary>复用严格failure门禁并拒绝会话复用选项；不提供默认凭据或目标降级。</summary>
    private static string Connection()
    {
        var text = PostgreSqlIntegrationEnvironment.GetFailureConnectionString(); var settings = new NpgsqlConnectionStringBuilder(text);
        Assert.IsFalse(settings.Multiplexing); Assert.IsFalse(settings.NoResetOnClose); return text;
    }

    /// <summary>无池测试连接用于确认物理backend退出；生产Store仍是原实际类和连接生命周期。</summary>
    private static DbSession Open(string connection) => new(new PostgreSqlConnectionFactory(
        new NpgsqlConnectionStringBuilder(connection) { Pooling = false }.ConnectionString));

    /// <summary>候选独占ApplicationName和非池连接，目录证据不依赖逻辑任务或Sleep。</summary>
    private static string CandidateConnection(string connection, string application) =>
        new NpgsqlConnectionStringBuilder(connection) { ApplicationName = application, Pooling = false }.ConnectionString;

    /// <summary>无凭据预登记在初始化DML前执行，早期角色未知会在只读发现后补全。</summary>
    private void Register(string connection, Guid uid, Guid? link, Guid? role, string policy) =>
        TestContext.WriteLine(JsonSerializer.Serialize(new { Database = new NpgsqlConnectionStringBuilder(connection).Database, User = uid, UserRole = link, Role = role, Policy = policy }));

    /// <summary>固定角色表SRE技术锁只借用父事务，不代替生产候选首锁。</summary>
    private static Task<int> RoleLock(DbSession session, CancellationToken token) =>
        session.Connection.ExecuteAsync(new CommandDefinition($"LOCK TABLE {Table<SysRole>()} IN SHARE ROW EXCLUSIVE MODE;", transaction: session.Transaction, cancellationToken: token));

    /// <summary>必须存在唯一有效管理员编码；不新增IsSystem业务资格，也不造/修改角色。</summary>
    private static async Task<SysRole> ExistingRole(DbSession session, CancellationToken token)
    {
        var ids = (await session.Connection.QueryAsync<Guid>(new CommandDefinition(
            $"SELECT uid FROM {Table<SysRole>()} WHERE rtrim(role_code,' ') COLLATE public.erp_dictionary_ci='SYSTEM_ADMIN' AND is_active=TRUE AND COALESCE(is_deleted,FALSE)=FALSE",
            transaction: session.Transaction, cancellationToken: token))).AsList();
        Assert.AreEqual(1, ids.Count, "严格前置：必须已有唯一有效SYSTEM_ADMIN角色；本例不修改角色或结构。");
        return await Read<SysRole>(session, ids[0]);
    }

    /// <summary>实际目录屏障确认候选纯LOCK等待；是否读到锁后提交事实由后续真实读取验证。</summary>
    private static async Task<int> ObserveRoleWait(DbSession observer, DbSession owner, string application, Task pending, CancellationToken token)
    {
        while (true)
        {
            var pid = await observer.Connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition($"""
                SELECT a.pid FROM pg_stat_activity a
                WHERE a.application_name=@Application AND a.state='active' AND a.wait_event_type='Lock'
                  AND a.backend_xid IS NULL AND a.query ILIKE 'LOCK TABLE%SHARE ROW EXCLUSIVE%'
                  AND EXISTS(SELECT 1 FROM pg_locks l WHERE l.pid=a.pid AND l.locktype='relation'
                    AND l.relation=CAST(@RoleTable AS regclass) AND l.mode='ShareRowExclusiveLock' AND NOT l.granted)
                  AND EXISTS(SELECT 1 FROM pg_locks l WHERE l.pid=@Owner AND l.locktype='relation'
                    AND l.relation=CAST(@RoleTable AS regclass) AND l.mode='ShareRowExclusiveLock' AND l.granted)
                """, new { Application = application, Owner = Pid(owner), RoleTable = Table<SysRole>() }, cancellationToken: token));
            if (pid.HasValue) return pid.Value;
            if (pending.IsCompleted) Assert.Fail("候选在确认角色首锁等待之前已结束；当前无首锁Store应暴露此缺口。");
            await Task.Delay(20, token);
        }
    }

    /// <summary>候选非池连接必须真正退出，任务完成不能替代物理连接/锁归还证据。</summary>
    private static async Task WaitDisconnected(DbSession observer, string application, int pid, CancellationToken token)
    {
        while (await observer.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid=@Pid OR application_name=@Application)",
            new { Pid = pid, Application = application }, cancellationToken: token))) await Task.Delay(20, token);
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM pg_locks WHERE pid=@Pid", new { Pid = pid }, cancellationToken: token)));
    }

    /// <summary>原生物理PID不以逻辑session身份代替。</summary>
    private static int Pid(DbSession session) => ((NpgsqlConnection)session.Connection).ProcessID;

    /// <summary>仅固定实体元数据物理名，避免旧SQL复数表名污染PG。</summary>
    private static string Table<T>()
    {
        var map = EntityMetadataCache.GetMetadata(typeof(T));
        return $"{PostgreSqlNames.Quote(map.SchemaName)}.{PostgreSqlNames.Quote(map.TableName)}";
    }

    /// <summary>完整typed实体夹具只写预登记随机User/UR，空文本显式text避免驱动推断。</summary>
    private static async Task Insert(DbSession session, BaseEntity entity, CancellationToken token)
    {
        var map = EntityMetadataCache.GetMetadata(entity.GetType());
        var sql = $"INSERT INTO {PostgreSqlNames.Quote(map.SchemaName)}.{PostgreSqlNames.Quote(map.TableName)} ({string.Join(',',map.Columns.Select(x=>PostgreSqlNames.Quote(x.ColumnName)))}) VALUES ({string.Join(',',map.Columns.Select(x=>'@'+x.ParameterName))})";
        Assert.AreEqual(1, await session.Connection.ExecuteAsync(new CommandDefinition(sql, new Parameters(entity, map), session.Transaction, cancellationToken: token)));
    }

    /// <summary>按UID读取所有持久化字段，可借用owner父事务确认初始化值。</summary>
    private static Task<T> Read<T>(DbSession session, Guid uid) where T : BaseEntity
    {
        var map = EntityMetadataCache.GetMetadata(typeof(T));
        var columns = string.Join(',', map.Columns.Select(x => PostgreSqlNames.Quote(x.ColumnName)+" AS "+PostgreSqlNames.Quote(x.PropertyName)));
        return session.Connection.QuerySingleAsync<T>(new CommandDefinition($"SELECT {columns} FROM {Table<T>()} WHERE uid=@Uid", new { Uid = uid }, session.Transaction));
    }

    /// <summary>只排除原五写列，其余全列相等；凭据不得进入断言差异输出。</summary>
    private static void Compare<T>(T before, T after, string[] excluded) where T : BaseEntity
    {
        foreach (var column in EntityMetadataCache.GetMetadata(typeof(T)).Columns.Where(x => !excluded.Contains(x.PropertyName)))
            Assert.IsTrue(Equals(column.Property.GetValue(before), column.Property.GetValue(after)), $"字段{column.PropertyName}不得变化；不输出字段值。");
    }

    /// <summary>精确身份残留计数不查询全库凭据。</summary>
    private static Task<int> Count<T>(DbSession session, Guid uid) =>
        session.Connection.ExecuteScalarAsync<int>($"SELECT count(*) FROM {Table<T>()} WHERE uid=@Uid", new { Uid = uid });

    /// <summary>元数据参数保留时间/版本类型；仅为可空字符串明确text。</summary>
    private sealed class Parameters(BaseEntity entity, EntityMetadata map) : SqlMapper.IDynamicParameters
    {
        /// <summary>每命令独立参数，不接管事务或写其他事实。</summary>
        public void AddParameters(IDbCommand command, SqlMapper.Identity identity)
        {
            PostgreSqlParameters.Entity(entity, map).AddParameters(command, identity);
            foreach (var column in map.Columns.Where(x => x.Property.PropertyType == typeof(string)))
                ((NpgsqlParameter)command.Parameters[column.ParameterName]!).NpgsqlDbType = NpgsqlDbType.Text;
        }
    }
}

