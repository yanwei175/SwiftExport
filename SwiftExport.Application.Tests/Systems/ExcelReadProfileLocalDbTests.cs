using Dapper;
using Npgsql;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Systems.ExcelReadProfiles;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles;
using SwiftExport.Core.Entities.Systems.ExcelReadProfiles;
using SwiftExport.Core.Interfaces.Systems.ExcelReadProfiles;
using SwiftExport.Application.Tests.Database;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Systems.ExcelReadProfiles;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Services.Auth;
using Proxy = SwiftExport.Application.Tests.Shipping.PackingListPostingCoordinatorTests.Proxy;

namespace SwiftExport.Application.Tests.Systems;

/// <summary>沿用原配置生命周期断言，在既有隔离PG库验证真实仓储；只写随机新分组，不部署结构或清理数据。</summary>
[TestClass]
[DoNotParallelize]
[TestCategory("PostgreSqlIntegration")]
public sealed class ExcelReadProfilePostgreSqlTests
{
    private static readonly Dictionary<string, (Guid[] Ids, string Snapshot)> OriginalRows = [];
    private static long OriginalSequence;
    private static string? CancellationWait;

    /// <summary>写入前冻结现有50表原身份和完整行摘要；其他活动事务存在时停止，不初始化或重置数据库。</summary>
    [ClassInitialize]
    public static async Task CaptureOriginalRowsAsync(TestContext context)
    {
        using var session = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        Assert.AreEqual(0, await session.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid() AND backend_type='client backend' AND (state<>'idle' OR xact_start IS NOT NULL)"), "隔离写入必须串行且没有其他活动事务。");
        var tables = (await session.Connection.QueryAsync<string>("SELECT table_name FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE' ORDER BY table_name")).ToArray();
        Assert.AreEqual(50, tables.Length, "使用既有结构，禁止初始化测试库。");
        foreach (var table in tables)
        {
            var ids = (await session.Connection.QueryAsync<Guid>($"SELECT uid FROM public.{PostgreSqlNames.Quote(table)}")).ToArray();
            OriginalRows.Add(table, (ids, await SnapshotAsync(session, table, ids)));
        }
        OriginalSequence = await session.Connection.ExecuteScalarAsync<long>("SELECT last_value FROM public.inventory_ledger_sequence");
    }

    /// <summary>结束后记录全部原行及新身份、库存序列和取消等待证据；成功事实保留，任何原行变化均使验收失败。</summary>
    [ClassCleanup]
    public static async Task VerifyOriginalRowsAsync()
    {
        if (OriginalRows.Count == 0) return;
        using var session = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        var evidence = new List<object>();
        var unchanged = true;
        foreach (var (table, original) in OriginalRows)
        {
            var after = await SnapshotAsync(session, table, original.Ids);
            var parameters = new PostgreSqlParameters(); parameters.AddIds("OldIds", original.Ids);
            var added = (await session.Connection.QueryAsync<Guid>($"SELECT uid FROM public.{PostgreSqlNames.Quote(table)} WHERE NOT(uid=ANY(@OldIds)) ORDER BY uid", parameters)).ToArray();
            evidence.Add(new { Table = table, OldCount = original.Ids.Length, Before = original.Snapshot, After = after, AddedIds = added });
            unchanged &= original.Snapshot == after;
        }
        var sequence = await session.Connection.ExecuteScalarAsync<long>("SELECT last_value FROM public.inventory_ledger_sequence");
        Directory.CreateDirectory("TestResults/postgresql-migration");
        await File.WriteAllTextAsync("TestResults/postgresql-migration/p09-r16-excel-data-protection.json", JsonSerializer.Serialize(new
        {
            RecordedAt = DateTimeOffset.Now, Database = "dv_next_pg_failure_tests_20261004",
            SequenceBefore = OriginalSequence, SequenceAfter = sequence, CancellationWait, Tables = evidence
        }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.IsTrue(unchanged, "原记录被修改或删除；逐表摘要已保存。");
        Assert.AreEqual(OriginalSequence, sequence, "Excel配置测试不得推进库存序列。");
    }

    /// <summary>仅按原身份数组比较完整行，不按时间或测试名称排除原数据；表名来自本库目录并引用转义。</summary>
    private static async Task<string> SnapshotAsync(DbSession session, string table, Guid[] ids)
    {
        var parameters = new PostgreSqlParameters(); parameters.AddIds("OldIds", ids);
        return await session.Connection.ExecuteScalarAsync<string>($"""
            SELECT count(*)::text||':'||md5(COALESCE(string_agg(md5(to_jsonb(t)::text),'' ORDER BY uid),''))
            FROM public.{PostgreSqlNames.Quote(table)} t WHERE uid=ANY(@OldIds)
            """, parameters) ?? throw new AssertFailedException("原记录保护摘要为空。");
    }

    /// <summary>保留创建、分步保存、软删除、并发版本、字段写后失败及SQL等待取消的原断言；不连接运行或备份库。</summary>
    [TestMethod]
    public async Task ProfileLifecycle_IsAtomicAndVersioned()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        var group = "R16_" + Guid.NewGuid().ToString("N");
        var otherGroup = "R16B_" + Guid.NewGuid().ToString("N");
        using var session = new DbSession(new PostgreSqlConnectionFactory(connection));
        using var logs = LoggerFactory.Create(_ => { });
        var user = new TestUser();
        var repository = new PostgreSqlExcelReadProfileRepository(new RepositoryBaseContext(session, logs, new AuditStampService(user), user));
        // 全部调用透传真实仓储；仅字段真实写完后注入服务器错误，替代原AFTER UPDATE失败触发器，不创建DDL。
        var guarded = Proxy.Make<IExcelReadProfileRepository>((name, args) => name == nameof(IExcelReadProfileRepository.SaveFieldAsync)
            ? SaveFieldWithFailureAsync(repository, session, (ExcelReadField)args[0]!, (bool)args[1]!, (CancellationToken)args[2]!)
            : typeof(IExcelReadProfileRepository).GetMethod(name)!.Invoke(repository, args));
        var service = new ExcelReadProfileService(guarded, new ServiceBaseContext(session, logs));
        Assert.AreEqual(0, await session.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM pg_constraint c JOIN pg_namespace n ON n.oid=c.connamespace WHERE n.nspname='public' AND c.contype='f'"));
        Assert.AreEqual(0, await session.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.excel_read_profile WHERE group_code=ANY(@Groups)", new { Groups = new[] { group, otherGroup } }));
        var create = await service.CreateAsync(new CreateExcelReadProfileRequest { GroupCode = group, ConfigName = " 客户 A " }, default);
        Assert.IsTrue(create.Success, create.Message);
        var uid = create.Data.Uid;
        Assert.AreEqual(1, create.Data.Version);
        Assert.AreEqual("客户 A", create.Data.ConfigName);
        Assert.AreEqual("test", create.Data.CreatedBy);
        Assert.IsFalse(create.Data.CanRead);
        Assert.IsNull(session.Transaction);
        var duplicate = await service.CreateAsync(new CreateExcelReadProfileRequest { GroupCode = group, ConfigName = "客户 A" }, default);
        Assert.AreEqual("EXCEL_READ_DUPLICATE_NAME", duplicate.Error.Code);
        Assert.IsNull(session.Transaction);
        Assert.IsTrue((await service.CreateAsync(new CreateExcelReadProfileRequest { GroupCode = otherGroup, ConfigName = "客户 A" }, default)).Success);

        var request = Request(1, [Field("Line.Sku", "a$8"), Field("Line.Quantity", "$D$8")]);
        var partial = await service.UpdateAsync(uid, request, default);
        Assert.IsTrue(partial.Success, partial.Message);
        Assert.AreEqual(2, partial.Data.Version);
        Assert.IsFalse(partial.Data.CanRead);
        Assert.AreEqual("A8", partial.Data.Fields.Single(field => field.TargetFieldKey == "Line.Sku").CellAddress);
        request.ExpectedVersion = 2;
        request.EndMode = 1;
        request.LastRowColumn = "a";
        request.Fields[1].WorksheetName = null!;
        var complete = await service.UpdateAsync(uid, request, default);
        Assert.IsTrue(complete.Success, complete.Message);
        Assert.IsTrue(complete.Data.CanRead);
        Assert.AreEqual("A", complete.Data.LastRowColumn);
        Assert.IsNull(complete.Data.Fields.Single(field => field.TargetFieldKey == "Line.Quantity").WorksheetName);
        Assert.AreEqual(1, await session.Connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM public.excel_read_field WHERE profile_uid=@Uid AND NOT is_deleted AND worksheet_name IS NULL", new { Uid = uid }));
        var stale = await service.UpdateAsync(uid, request, default);
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, stale.Error.Code);
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, (await service.DeleteAsync(uid, 2, default)).Error.Code);

        // 字段缺席明确表示清除，保留字段的内部身份不能因完整快照保存而变化。
        var oldField = (await repository.GetFieldsAsync(uid, default)).Single(field => field.TargetFieldKey == "Line.Sku");
        request.ExpectedVersion = 3;
        request.Fields.RemoveAt(1);
        var removed = await service.UpdateAsync(uid, request, default);
        Assert.IsTrue(removed.Success, removed.Message);
        Assert.HasCount(1, removed.Data.Fields);
        Assert.AreEqual(oldField.Uid, (await repository.GetFieldsAsync(uid, default)).Single().Uid);
        Assert.AreEqual(1, await session.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.excel_read_field WHERE profile_uid=@Uid AND is_deleted", new { Uid = uid }));

        // 后半段字段写入失败时，先写入的主版本、配置名和所有字段都必须回滚。
        request.ExpectedVersion = 4;
        request.ConfigName = "不能部分生效";
        request.Fields[0].WorksheetName = "FAIL";
        var failed = await service.UpdateAsync(uid, request, default);
        Assert.AreEqual(ServiceErrorCodes.Unexpected, failed.Error.Code);
        Assert.IsNull(session.Transaction);
        var afterFailure = await service.GetAsync(uid, default);
        Assert.AreEqual(4, afterFailure.Data.Version);
        Assert.AreEqual("客户 A", afterFailure.Data.ConfigName);
        Assert.AreEqual("订单", afterFailure.Data.Fields[0].WorksheetName);

        // 两个请求使用同一个主版本时只允许一个成功，另一个不得覆盖字段。
        using (var otherSession = new DbSession(new PostgreSqlConnectionFactory(connection)))
        {
            var otherRepo = new PostgreSqlExcelReadProfileRepository(new RepositoryBaseContext(otherSession, logs, new AuditStampService(user), user));
            var otherService = new ExcelReadProfileService(otherRepo, new ServiceBaseContext(otherSession, logs));
            var first = Request(4, [Field("Line.Sku", "A8")]);
            first.EndMode = 1; first.LastRowColumn = "A"; first.ConfigName = "并发 A";
            var second = Request(4, [Field("Line.Sku", "B8")]);
            second.EndMode = 1; second.LastRowColumn = "B"; second.ConfigName = "并发 B";
            var outcomes = await Task.WhenAll(service.UpdateAsync(uid, first, default), otherService.UpdateAsync(uid, second, default));
            Assert.AreEqual(1, outcomes.Count(result => result.Success));
            Assert.AreEqual(ServiceErrorCodes.VersionConflict, outcomes.Single(result => !result.Success).Error.Code);
        }

        // 服务不能接管或释放调用方已有事务。
        using (var outer = session.Connection.BeginTransaction())
        {
            session.Transaction = outer;
            Assert.AreEqual(ServiceErrorCodes.Unexpected, (await service.GetAsync(uid, default)).Error.Code);
            Assert.AreSame(outer, session.Transaction);
            outer.Rollback();
            session.Transaction = null;
        }
        var beforeDelete = (await service.GetAsync(uid, default)).Data;
        Assert.AreEqual(5, beforeDelete.Version);
        // SQL 正在字段更新阶段等待时取消，已经写入的主版本也必须回滚。
        var cancelRequest = Request(5, [Field("Line.Sku", "C8")]);
        cancelRequest.EndMode = 1;
        cancelRequest.LastRowColumn = "C";
        cancelRequest.Fields[0].WorksheetName = "SLOW";
        using (var holder = new DbSession(new PostgreSqlConnectionFactory(connection)))
        using (var held = holder.Connection.BeginTransaction())
        using (var cancellation = new CancellationTokenSource())
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            // 只锁住本批新字段；待真实UPDATE阻塞后取消，验证此前主记录写入也回滚，不以延时模拟SQL取消。
            await holder.Connection.ExecuteScalarAsync<Guid>("SELECT uid FROM public.excel_read_field WHERE uid=@Uid FOR UPDATE", new { oldField.Uid }, held);
            var pending = service.UpdateAsync(uid, cancelRequest, cancellation.Token);
            var wasCancelled = false;
            try
            {
                var waiter = ((NpgsqlConnection)session.Connection).ProcessID;
                while (!await holder.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT pg_backend_pid()=ANY(pg_blocking_pids(@Pid))", new { Pid = waiter }, held, cancellationToken: deadline.Token)))
                    await Task.Delay(25, deadline.Token);
                CancellationWait = await holder.Connection.ExecuteScalarAsync<string>("SELECT json_build_object('holder',pg_backend_pid(),'waiter',@Pid,'blockers',pg_blocking_pids(@Pid))::text", new { Pid = waiter }, held);
                cancellation.Cancel();
                try { await pending; }
                catch (OperationCanceledException) { wasCancelled = true; }
            }
            finally
            {
                cancellation.Cancel();
                held.Rollback();
                // 观察超时也必须接回实际SQL任务，不能留下后台写入再开始下一场景。
                try { await pending; } catch (OperationCanceledException) { }
            }
            Assert.IsTrue(wasCancelled, "SQL 请求取消必须透传给调用方");
        }
        Assert.IsNull(session.Transaction);
        var afterCancellation = (await service.GetAsync(uid, default)).Data;
        Assert.AreEqual(5, afterCancellation.Version);
        Assert.AreEqual(beforeDelete.ConfigName, afterCancellation.ConfigName);
        Assert.AreEqual(beforeDelete.Fields[0].CellAddress, afterCancellation.Fields[0].CellAddress);
        Assert.AreEqual("订单", afterCancellation.Fields[0].WorksheetName);
        Assert.IsTrue((await service.DeleteAsync(uid, 5, default)).Success);
        Assert.AreEqual(ServiceErrorCodes.NotFound, (await service.GetAsync(uid, default)).Error.Code);
        Assert.HasCount(0, (await service.ListAsync(group, default)).Data);
        Assert.HasCount(0, await repository.GetFieldsAsync(uid, default));
        Assert.AreEqual(6, await session.Connection.ExecuteScalarAsync<int>("SELECT version FROM public.excel_read_profile WHERE uid=@Uid", new { Uid = uid }));
        Assert.IsTrue((await service.CreateAsync(new CreateExcelReadProfileRequest { GroupCode = group, ConfigName = "客户 A" }, default)).Success);
        Assert.IsNull(session.Transaction);
    }

    /// <summary>仅在本批FAIL字段真实写入后制造PG除零错误，保留原写后失败时主版本与字段一起回滚的验证边界。</summary>
    private static async Task SaveFieldWithFailureAsync(IExcelReadProfileRepository repository, DbSession session, ExcelReadField field, bool isNew, CancellationToken token)
    {
        await repository.SaveFieldAsync(field, isNew, token);
        if (field.WorksheetName == "FAIL")
            await session.Connection.ExecuteAsync(new CommandDefinition("SELECT 1/0", transaction: session.Transaction, cancellationToken: token));
    }

    private static UpdateExcelReadProfileRequest Request(int version, List<ExcelReadFieldDto> fields) => new()
        { ExpectedVersion = version, ConfigName = "客户 A", Fields = fields };
    private static ExcelReadFieldDto Field(string key, string address) => new()
        { TargetFieldKey = key, WorksheetName = "订单", CellAddress = address, IsDownward = true };

    /// <summary>仅为本批新配置的审计提供原测试身份，不修改账号或模拟权限授予。</summary>
    private sealed class TestUser : ICurrentUser
    {
        public string UserName => "test";
        public Guid? UserId => Guid.Empty;
        public bool IsAuthenticated => true;
    }
}
