using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Tools.DbSeed;
using SwiftExport.Application.Services.Auth.Passwords;

namespace SwiftExport.Application.Tests.Database;

/// <summary>既有failure库目录同步原生测试；仅主控串行执行，不造空目录，不以外部父事务冒称工具可回滚。</summary>
[TestClass, DoNotParallelize]
public sealed class PostgreSqlPermissionCatalogSyncMigrationIntegrationTests
{
    /// <summary>输出无凭据全定义快照/精确UID及变更政策，主控授权后才可运行展示变更场景。</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>通过真实工具进程验证创建/重复引导/拒绝提升；仅清理本例确认成功的新账号和关联，保护全部既有账号。</summary>
    [TestMethod]
    public async Task BootstrapEntry_CreatesOnceAndRefusesOrdinaryUserPromotion()
    {
        var connection=Connection(); using var observer=Open(connection); var baseline=await Snapshot(observer);
        RequireCodeDisplayBaseline(await Catalog(observer));
        var userName="P06Bootstrap-"+Guid.NewGuid().ToString("N"); var ordinaryName=userName+"-ordinary";
        var ordinaryUid=Guid.NewGuid(); Guid? adminUid=null; var ordinaryCreated=false;
        var hash=new BcryptPasswordHasher().Hash("P06-Only-Random-Fixture-"+Guid.NewGuid().ToString("N"));
        try
        {
            Assert.AreEqual(0,await RunBootstrapProcess(connection,userName,hash));
            adminUid=await observer.Connection.QuerySingleAsync<Guid>("SELECT uid FROM public.sys_user WHERE user_name=@Name AND created_by='DbSeed'",new{Name=userName});
            Assert.AreEqual(1,await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.sys_user_role ur JOIN public.sys_role r ON r.uid=ur.role_id WHERE ur.user_id=@Id AND r.role_code='SYSTEM_ADMIN' AND ur.is_deleted=FALSE",new{Id=adminUid.Value}));
            var original=await observer.Connection.QuerySingleAsync<string>("SELECT to_jsonb(u)::text FROM public.sys_user u WHERE uid=@Id",new{Id=adminUid.Value});
            Assert.AreEqual(0,await RunBootstrapProcess(connection,userName,hash+"-must-not-replace"));
            Assert.AreEqual(original,await observer.Connection.QuerySingleAsync<string>("SELECT to_jsonb(u)::text FROM public.sys_user u WHERE uid=@Id",new{Id=adminUid.Value}));

            Assert.AreEqual(1,await observer.Connection.ExecuteAsync("""
                INSERT INTO public.sys_user(uid,version,is_deleted,created_by,user_name,password_hash,credential_version,is_active)
                VALUES(@Uid,1,FALSE,'P06BootstrapFixture',@Name,@Hash,1,TRUE)
                """,new{Uid=ordinaryUid,Name=ordinaryName,Hash=hash})); ordinaryCreated=true;
            var ordinary=await observer.Connection.QuerySingleAsync<string>("SELECT to_jsonb(u)::text FROM public.sys_user u WHERE uid=@Id",new{Id=ordinaryUid});
            Assert.AreNotEqual(0,await RunBootstrapProcess(connection,ordinaryName,hash));
            Assert.AreEqual(ordinary,await observer.Connection.QuerySingleAsync<string>("SELECT to_jsonb(u)::text FROM public.sys_user u WHERE uid=@Id",new{Id=ordinaryUid}));
            Assert.AreEqual(0,await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.sys_user_role WHERE user_id=@Id",new{Id=ordinaryUid}));
        }
        finally
        {
            // 没有确认创建成功的身份不能自动删除；固定随机UID/名称/创建标记只允许清理本例拥有的行。
            using var cleanup=observer.Connection.BeginTransaction();
            try
            {
                if(adminUid.HasValue)
                {
                    await observer.Connection.ExecuteAsync("DELETE FROM public.sys_user_role WHERE user_id=@Id AND created_by='DbSeed'",new{Id=adminUid.Value},cleanup);
                    await observer.Connection.ExecuteAsync("DELETE FROM public.sys_user WHERE uid=@Id AND user_name=@Name AND created_by='DbSeed'",new{Id=adminUid.Value,Name=userName},cleanup);
                }
                if(ordinaryCreated) await observer.Connection.ExecuteAsync("DELETE FROM public.sys_user WHERE uid=@Id AND user_name=@Name AND created_by='P06BootstrapFixture'",new{Id=ordinaryUid,Name=ordinaryName},cleanup);
                cleanup.Commit();
            }
            catch {cleanup.Rollback();throw;}
        }
        Same(baseline,await Snapshot(observer));
    }

    /// <summary>使用工具真实入口和独立子进程环境传递凭据，不放命令行或日志；超时仅终止本例子进程，不宣称提交结果。</summary>
    private static async Task<int> RunBootstrapProcess(string connection,string userName,string hash)
    {
        DirectoryInfo? root=new(AppContext.BaseDirectory);
        while(root is not null&&!File.Exists(Path.Combine(root.FullName,"_tools","DbSeed","Program.cs")))root=root.Parent;
        if(root is null)throw new DirectoryNotFoundException("无法定位DbSeed真实入口。");
        var executable=Path.Combine(root.FullName,"_tools","DbSeed","bin","Debug","net10.0","DbSeed.dll");
        Assert.IsTrue(File.Exists(executable),"主控须先编译工具入口，不自动构建或使用过期副本。");
        var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add(executable);start.Environment["SWIFTEXPORT_DBSEED_CONNECTION"]=connection;
        start.Environment["SWIFTEXPORT_DBSEED_USERNAME"]=userName;start.Environment["SWIFTEXPORT_DBSEED_PASSWORD_HASH"]=hash;
        start.Environment.Remove("SWIFTEXPORT_DBSEED_NICKNAME");
        using var process=Process.Start(start)??throw new InvalidOperationException("未启动测试工具进程。");
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try{await process.WaitForExitAsync(deadline.Token);await Task.WhenAll(output,error);return process.ExitCode;}
        catch(OperationCanceledException)
        {
            if(!process.HasExited)process.Kill();
            await process.WaitForExitAsync();await Task.WhenAll(output,error);throw;
        }
    }

    /// <summary>完整dry-run同首锁事务但50表摘要不变，不写任何目录或授权。</summary>
    [TestMethod]
    public async Task DryRun_HasNoChangesAcrossFiftyTables()
    {
        using var session = Open(Connection()); var before = await Snapshot(session);
        await new PostgreSqlPermissionCatalogSync(session).RunAsync(true);
        Assert.IsNull(session.Transaction); Same(before, await Snapshot(session));
    }

    /// <summary>已有父事务明确拒绝接管，无工具SQL/写入，原父事务仍由调用方回滚。</summary>
    [TestMethod]
    public async Task ExistingTransaction_IsRejectedWithoutChanges()
    {
        using var session = Open(Connection()); var before = await Snapshot(session);
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted); session.Transaction = transaction;
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => new PostgreSqlPermissionCatalogSync(session).RunAsync(true));
            Assert.AreSame(transaction, session.Transaction); transaction.Rollback();
        }
        finally { session.Transaction = null; }
        Same(before, await Snapshot(session));
    }

    /// <summary>完整原目录展示一致才可授权变更本例三列；工具在首role等待后看到owner提交差异并恢复代码展示。</summary>
    [TestMethod]
    public async Task RoleWait_SeesCommittedDisplayChangeAndPreservesOtherColumns()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_CATALOG_SYNC_MUTATION_AUTHORIZED") != "1")
            Assert.Inconclusive("未获主控对本例完整目录快照与三展示列恢复范围的显式授权；不连接/改既有目录。");
        var connection = Connection(); using var owner = Open(connection); using var observer = Open(connection);
        var catalog = await Catalog(owner); RequireCodeDisplayBaseline(catalog);
        var target = catalog.Single(x => x.PermissionKey == PermissionCatalog.All[0].PermissionKey);
        var baseline = await Snapshot(observer); var application = "P06SYNC-" + Guid.NewGuid().ToString("N");
        TestContext.WriteLine(JsonSerializer.Serialize(new { Phase = "展示变更前完整快照", Database = new NpgsqlConnectionStringBuilder(connection).Database,
            Target = target.Uid, Definitions = catalog, Policy = "只改/恢复本例DisplayName/GroupKey/SortOrder；提交未知保留清单" }));
        using var candidate = Open(connection, application); using var transaction = owner.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        owner.Transaction = transaction; using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task? pending = null; var commitAttempted = false; var committed = false; var finished = false; var verified = false;
        try
        {
            await Lock(owner, Table<SysRole>(), deadline.Token); await Lock(owner, Table<SysPermission>(), deadline.Token);
            Assert.AreEqual(1, await owner.Connection.ExecuteAsync(new CommandDefinition($"""
                UPDATE {Table<SysPermission>()} SET display_name=@Name,group_key=@Group,sort_order=@Order WHERE uid=@Uid
                """, new { Name = "P06同步竞争夹具", Group = "P06同步竞争夹具", Order = -123, target.Uid }, transaction, cancellationToken: deadline.Token)));
            pending = new PostgreSqlPermissionCatalogSync(candidate).RunAsync(false, deadline.Token);
            await ObserveWait(observer, owner, candidate, Table<SysRole>(), pending, deadline.Token);
            commitAttempted = true;
            try { transaction.Commit(); committed = true; }
            catch { TestContext.WriteLine("owner提交结果未知，按完整预登记清单核验，不自动恢复/删除。"); throw; }
            finally { owner.Transaction = null; }
            await pending; finished = true; Assert.IsNull(candidate.Transaction);
            Compare(target, (await Catalog(observer)).Single(x => x.Uid == target.Uid));
            Same(baseline, await Snapshot(observer));
            verified = true;
        }
        finally
        {
            deadline.Cancel();
            try { if (!commitAttempted) transaction.Rollback(); }
            finally
            {
                try { transaction.Dispose(); }
                finally { owner.Transaction = null; if (pending is not null) { try { await pending; } catch (OperationCanceledException) when (deadline.IsCancellationRequested) { } } }
            }
            // 只有确认owner提交、候选任务已收、且没有未知提交时才进入精确授权恢复；其余仅留清单。
            if (committed && !finished && candidate.Transaction is null)
                TestContext.WriteLine("owner已提交但候选未确认成功：可能工具提交未知，本例展示保持清单交主控独立核验，不自动覆盖。");
            if (verified)
                TestContext.WriteLine(JsonSerializer.Serialize(new { Phase = "工具恢复原代码三展示列且全库摘要不变", Target = target.Uid }));
            else if (finished) TestContext.WriteLine("工具命令已确认完成，但快照断言未全部通过；保留完整清单交主控，不自动覆盖。");
        }
    }

    /// <summary>首role或后permission纯LOCK等待取消，工具回滚清事务，再释放owner供第三连接有界重取。</summary>
    [TestMethod]
    [DataRow("role")]
    [DataRow("permission")]
    public async Task CancelledLock_ReleasesAndAllowsThirdBackend(string kind)
    {
        var connection = Connection(); using var owner = Open(connection); using var observer = Open(connection);
        var before = await Snapshot(observer); var table = kind == "role" ? Table<SysRole>() : Table<SysPermission>();
        using var ownerTransaction = owner.Connection.BeginTransaction(IsolationLevel.ReadCommitted); owner.Transaction = ownerTransaction;
        var application = "P06SYNC-" + Guid.NewGuid().ToString("N"); using var candidate = Open(connection, application);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); using var cancel = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        Task? pending = null; var rolledBack = false;
        try
        {
            // permission场景owner只锁该表、无DML且不再请求role，不制造权限→角色反向等待。
            await Lock(owner, table, deadline.Token);
            pending = new PostgreSqlPermissionCatalogSync(candidate).RunAsync(true, cancel.Token);
            await ObserveWait(observer, owner, candidate, table, pending, deadline.Token);
            cancel.Cancel(); await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
            Assert.IsNull(candidate.Transaction);
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT count(*) FROM pg_locks WHERE pid=@Pid AND locktype='relation' AND mode='ShareRowExclusiveLock'",
                new { Pid = Pid(candidate) }, cancellationToken: deadline.Token)));
            var candidatePid = Pid(candidate); candidate.Dispose();
            await Disconnected(observer, application, candidatePid, deadline.Token);
            ownerTransaction.Rollback(); rolledBack = true; owner.Transaction = null;
            using var third = Open(connection); using var thirdTransaction = third.Connection.BeginTransaction(IsolationLevel.ReadCommitted); third.Transaction = thirdTransaction;
            try
            {
                Assert.AreNotEqual(Pid(owner), Pid(third));
                await third.Connection.ExecuteAsync(new CommandDefinition("SET LOCAL lock_timeout='3s';", transaction: thirdTransaction, cancellationToken: deadline.Token));
                await Lock(third, table, deadline.Token); thirdTransaction.Rollback();
            }
            finally { third.Transaction = null; }
            Same(before, await Snapshot(observer));
        }
        finally
        {
            cancel.Cancel();
            try { if (!rolledBack) ownerTransaction.Rollback(); }
            finally
            {
                try { ownerTransaction.Dispose(); }
                finally { owner.Transaction = null; if (pending is not null) { try { await pending; } catch (OperationCanceledException) when (cancel.IsCancellationRequested) { } } }
            }
        }
    }

    /// <summary>只用既有合法停用/软删目录外行，完整dry-run保留全列；缺夹具便未运行，不造主数据。</summary>
    [TestMethod]
    public async Task ExistingExternalState_IsFullyPreserved()
    {
        using var session = Open(Connection()); var catalog = await Catalog(session);
        var target = catalog.FirstOrDefault(x => (x.IsDeleted || !x.IsActive) && !PermissionCatalog.All.Any(d => string.Equals(d.PermissionKey, x.PermissionKey, StringComparison.OrdinalIgnoreCase)));
        if (target is null) Assert.Inconclusive("没有既有合法停用/软删目录外定义；不造/清目录。");
        var before = await Snapshot(session); await new PostgreSqlPermissionCatalogSync(session).RunAsync(true);
        Compare(target, (await Catalog(session)).Single(x => x.Uid == target.Uid)); Same(before, await Snapshot(session));
    }

    /// <summary>只读既有大小写漂移验证原精确拼写拒绝；没有漂移便未运行，不修改Key造条件。</summary>
    [TestMethod]
    public async Task ExistingSpellingDrift_IsRejectedWithoutWriting()
    {
        using var session = Open(Connection()); var catalog = await Catalog(session);
        if (!catalog.Any(x => PermissionCatalog.All.Any(d => string.Equals(x.PermissionKey,d.PermissionKey,StringComparison.OrdinalIgnoreCase)
            && !string.Equals(x.PermissionKey,d.PermissionKey,StringComparison.Ordinal))))
            Assert.Inconclusive("没有既有拼写漂移；需主控独立库范围，不改现库Key。");
        Assert.AreEqual(catalog.Count,catalog.Select(x=>x.PermissionKey).Distinct(StringComparer.OrdinalIgnoreCase).Count(),"本例不能以重复键错误替代精确拼写拒绝。");
        var before = await Snapshot(session);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new PostgreSqlPermissionCatalogSync(session).RunAsync(true));
        StringAssert.Contains(error.Message,"大小写与代码不一致");
        Assert.IsNull(session.Transaction); Same(before, await Snapshot(session));
    }

    /// <summary>严格固定failure和安全会话选项，全部实库仅由主控启动。</summary>
    private static string Connection()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString(); var settings = new NpgsqlConnectionStringBuilder(connection);
        Assert.IsFalse(settings.Multiplexing); Assert.IsFalse(settings.NoResetOnClose); return connection;
    }
    /// <summary>非池原生会话配独占App，可核验实际backend退出。</summary>
    private static DbSession Open(string connection,string? application=null) => new(new PostgreSqlConnectionFactory(
        new NpgsqlConnectionStringBuilder(connection){Pooling=false,ApplicationName=application??"P06SYNC-observer"}.ConnectionString));
    /// <summary>固定元数据物理表，不沿旧SQL复数名称。</summary>
    private static string Table<T>() { var map=EntityMetadataCache.GetMetadata(typeof(T));return $"{PostgreSqlNames.Quote(map.SchemaName)}.{PostgreSqlNames.Quote(map.TableName)}"; }
    /// <summary>本例技术纯LOCK仅借用父事务。</summary>
    private static Task<int> Lock(DbSession session,string table,CancellationToken token)=>session.Connection.ExecuteAsync(new CommandDefinition(
        $"LOCK TABLE {table} IN SHARE ROW EXCLUSIVE MODE;",transaction:session.Transaction,cancellationToken:token));
    /// <summary>全字段定义快照用于主控精确授权和保护Version/审计/状态。</summary>
    private static async Task<List<SysPermission>> Catalog(DbSession session)
    {
        var map=EntityMetadataCache.GetMetadata(typeof(SysPermission));var columns=string.Join(",",map.Columns.Select(x=>PostgreSqlNames.Quote(x.ColumnName)+" AS "+PostgreSqlNames.Quote(x.PropertyName)));
        return (await session.Connection.QueryAsync<SysPermission>(new CommandDefinition($"SELECT {columns} FROM {Table<SysPermission>()};",transaction:session.Transaction))).AsList();
    }
    /// <summary>变更场景必须已有完整且展示一致代码目录，防止工具成功时新增全目录被误算夹具。</summary>
    private static void RequireCodeDisplayBaseline(List<SysPermission> catalog)
    {
        Assert.AreEqual(catalog.Count,catalog.Select(x=>x.PermissionKey).Distinct(StringComparer.OrdinalIgnoreCase).Count(),"原目录不能存在IgnoreCase重复。");
        for(var i=0;i<PermissionCatalog.All.Count;i++)
        {
            var d=PermissionCatalog.All[i];var matches=catalog.Where(x=>x.PermissionKey==d.PermissionKey).ToArray();Assert.AreEqual(1,matches.Length,"必须已存在每个精确代码键，不初始化缺失定义。");
            Assert.IsTrue(matches[0].DisplayName==d.DisplayName&&matches[0].GroupKey==d.GroupKey&&matches[0].SortOrder==i+1,"本例只授权已知一行三展示列，不接受其他全目录差异。");
        }
    }
    /// <summary>逐持久化字段保护，不输出账号或凭据值。</summary>
    private static void Compare(SysPermission before,SysPermission after)
    {foreach(var column in EntityMetadataCache.GetMetadata(typeof(SysPermission)).Columns)Assert.IsTrue(Equals(column.Property.GetValue(before),column.Property.GetValue(after)),$"定义字段{column.PropertyName}应原样。");}
    /// <summary>服务器端有序JSON摘要而非读取实际凭据；50表数量及内容仅主控静默串行库可比较。</summary>
    private static async Task<Dictionary<string,Digest>> Snapshot(DbSession session)
    {
        var names=(await session.Connection.QueryAsync<string>(new CommandDefinition("SELECT table_name FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE' ORDER BY table_name",transaction:session.Transaction))).AsList();
        Assert.AreEqual(50,names.Count,"要求本批已审批50表结构；不初始化或修改结构。");
        var result=new Dictionary<string,Digest>();
        foreach(var name in names)result.Add(name,await session.Connection.QuerySingleAsync<Digest>(new CommandDefinition(
            $"SELECT count(*) AS \"Count\",md5(COALESCE(string_agg(to_jsonb(t)::text,E'\\n' ORDER BY to_jsonb(t)::text),'')) AS \"Hash\" FROM public.{PostgreSqlNames.Quote(name)} t",transaction:session.Transaction)));
        return result;
    }
    /// <summary>摘要只报表名，不输出摘要或原凭据。</summary>
    private static void Same(Dictionary<string,Digest> before,Dictionary<string,Digest> after)
    {CollectionAssert.AreEquivalent(before.Keys.ToArray(),after.Keys.ToArray());foreach(var pair in before)Assert.IsTrue(pair.Value.Count==after[pair.Key].Count&&pair.Value.Hash==after[pair.Key].Hash,$"表{pair.Key}应零变化。");}
    /// <summary>摘要传输值不打印。</summary>
    private sealed record Digest(long Count,string Hash);
    /// <summary>原生PID用于锁目录证据。</summary>
    private static int Pid(DbSession session)=>((NpgsqlConnection)session.Connection).ProcessID;
    /// <summary>候选当前纯LOCK、同表未granted、owner granted及无backend_xid组成真实屏障。</summary>
    private static async Task ObserveWait(DbSession observer,DbSession owner,DbSession candidate,string table,Task pending,CancellationToken token)
    {
        Assert.AreNotEqual(Pid(owner),Pid(candidate));
        while(!await observer.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS(SELECT 1 FROM pg_stat_activity a WHERE a.pid=@Candidate AND a.state='active'
                AND a.wait_event_type='Lock' AND a.backend_xid IS NULL AND a.query ILIKE 'LOCK TABLE%SHARE ROW EXCLUSIVE%')
              AND EXISTS(SELECT 1 FROM pg_locks WHERE pid=@Candidate AND relation=CAST(@Table AS regclass) AND mode='ShareRowExclusiveLock' AND NOT granted)
              AND EXISTS(SELECT 1 FROM pg_locks WHERE pid=@Owner AND relation=CAST(@Table AS regclass) AND mode='ShareRowExclusiveLock' AND granted)
            """,new{Candidate=Pid(candidate),Owner=Pid(owner),Table=table},cancellationToken:token)))
        {if(pending.IsCompleted)Assert.Fail("未确认首锁等待，候选已结束；不能以任务完成代替协议。");await Task.Delay(20,token);}
    }
    /// <summary>调用方Dispose借用会话后确认非池物理连接退出，不把工具应清事务和调用方连接混淆。</summary>
    private static async Task Disconnected(DbSession observer,string application,int pid,CancellationToken token)
    {while(await observer.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid=@Pid OR application_name=@Application)",new{Pid=pid,Application=application},cancellationToken:token)))await Task.Delay(20,token);}
}

