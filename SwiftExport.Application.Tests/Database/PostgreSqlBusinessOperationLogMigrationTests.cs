using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NpgsqlTypes;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Auditing;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>复用既有 Dapper 记录夹具核对审计仓储投影、参数及先命令锁后 SQL 顺序，不冒充实库互斥验收。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>两个命令查询统一锁键并恢复完整实体；模块查询保留跨操作首条排序，三元查询保持单行契约。</summary>
    [TestMethod]
    public async Task AuditLog_CommandQueriesShareLockAndMapEntity()
    {
        using var fixture = new Fixture();
        var repository = AuditLogRepository(fixture);
        var commandId = Guid.NewGuid();
        fixture.Connection.Rows.Columns.Add("CommandId", typeof(Guid));
        fixture.Connection.Rows.Columns.Add("OperationType", typeof(string));
        fixture.Connection.Rows.Columns.Add("ChangedFieldsJson", typeof(string));
        fixture.Connection.Rows.Columns.Add("UserUid", typeof(Guid));
        fixture.Connection.Rows.Rows.Add(commandId, "Post", "{\"Fingerprint\":\"abc\"}", DBNull.Value);
        var result = await repository.GetByModuleCommandAsync("Inventory", commandId);
        Assert.AreEqual(commandId, result!.CommandId);
        Assert.AreEqual("Post", result.OperationType);
        Assert.AreEqual("{\"Fingerprint\":\"abc\"}", result.ChangedFieldsJson);
        Assert.IsNull(result.UserUid);
        StringAssert.Contains(fixture.Connection.Text, "ORDER BY occurred_date, uid LIMIT 1 FOR UPDATE");
        StringAssert.Contains(fixture.Connection.Text, "\"changed_fields_json\" AS \"ChangedFieldsJson\"");
        await repository.GetByCommandAsync("Inventory", "Post", commandId);
        StringAssert.Contains(fixture.Connection.Text, "operation_type=@OperationType AND command_id=@CommandId AND is_deleted=FALSE FOR UPDATE");
        Assert.AreEqual("Post", fixture.Connection.Values["OperationType"]);
        Assert.HasCount(4, fixture.Connection.Commands);
        StringAssert.Contains(fixture.Connection.Commands[0].Sql, "pg_try_advisory_xact_lock");
        StringAssert.Contains(fixture.Connection.Commands[2].Sql, "pg_try_advisory_xact_lock");
        Assert.AreEqual(fixture.Connection.Commands[0].Values["Key"], fixture.Connection.Commands[2].Values["Key"]);
        Assert.IsTrue(fixture.Connection.Commands.All(c => ReferenceEquals(fixture.Session.Transaction, c.Transaction)));
        fixture.Connection.Rows.Rows.Add(commandId, "Post", "{}", DBNull.Value);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.GetByCommandAsync("Inventory", "Post", commandId));
        fixture.Connection.Rows.Rows.Clear();
        Assert.IsNull(await repository.GetByModuleCommandAsync("Inventory", commandId));
        Assert.IsNull(await repository.GetByCommandAsync("Inventory", "Post", commandId));
    }

    /// <summary>插入先取得相同命令锁，保留身份、发生时间及 JSON 原文，实际影响行数原样返回。</summary>
    [TestMethod]
    public async Task AuditLog_InsertPreservesRawJsonAndTypedTime()
    {
        using var fixture = new Fixture();
        var repository = AuditLogRepository(fixture);
        var log = new BusinessOperationLog
        {
            CommandId = Guid.NewGuid(), Module = "Inventory", OperationType = "Post", OccurredDate = DateTime.Now,
            ChangedFieldsJson = "{ \"Fingerprint\": \"abc\" }", RelatedObjectsJson = "[ {\"Uid\":1} ]"
        };
        var uid = log.Uid;
        Assert.IsNull(await repository.GetByModuleCommandAsync(log.Module, log.CommandId));
        Assert.AreEqual(1, await repository.InsertAsync(log));
        Assert.AreEqual(fixture.Connection.Commands[0].Values["Key"], fixture.Connection.Commands[2].Values["Key"]);
        Assert.AreEqual(uid, log.Uid);
        Assert.AreEqual(uid, fixture.Connection.Values["Uid"]);
        Assert.AreEqual(log.ChangedFieldsJson, fixture.Connection.Values["ChangedFieldsJson"]);
        Assert.AreEqual(log.RelatedObjectsJson, fixture.Connection.Values["RelatedObjectsJson"]);
        Assert.AreEqual(DBNull.Value, fixture.Connection.Values["UserUid"]);
        Assert.AreEqual(NpgsqlDbType.Timestamp, fixture.Connection.Types["OccurredDate"]);
        Assert.AreEqual(NpgsqlDbType.Timestamp, fixture.Connection.Types["CreatedDate"]);
        Assert.AreEqual(DateTimeKind.Unspecified, ((DateTime)fixture.Connection.Values["OccurredDate"]!).Kind);
        StringAssert.Contains(fixture.Connection.Commands[0].Sql, "pg_try_advisory_xact_lock");
        StringAssert.Contains(fixture.Connection.Text, "INSERT INTO public.business_operation_log");
        fixture.Connection.ExecuteWrite = () => 0;
        Assert.AreEqual(0, await repository.InsertAsync(log));
        var failure = new InvalidOperationException("模拟审计写入错误");
        fixture.Connection.ExecuteWrite = () => throw failure;
        Assert.AreSame(failure, await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.InsertAsync(log)));
        Assert.AreEqual(0, fixture.Connection.Commits);
        Assert.AreEqual(0, fixture.Connection.Rollbacks);
    }

    /// <summary>普通追溯只执行一次读取，明确映射冻结用户名与 JSON，不要求事务或获取命令锁。</summary>
    [TestMethod]
    public async Task AuditLog_TraceMapsUserNameWithoutCommandLock()
    {
        using var fixture = new Fixture();
        fixture.Session.Transaction!.Dispose();
        fixture.Session.Transaction = null;
        fixture.Connection.Rows.Columns.Add("UserName", typeof(string));
        fixture.Connection.Rows.Columns.Add("RelatedObjectsJson", typeof(string));
        fixture.Connection.Rows.Rows.Add("历史操作人", "[]");
        var repository = AuditLogRepository(fixture);
        var aggregateUid = Guid.NewGuid();
        var trace = await repository.GetByAggregateAsync("Inventory", aggregateUid);
        Assert.AreEqual("历史操作人", trace.Single().UserName);
        Assert.AreEqual("[]", trace.Single().RelatedObjectsJson);
        Assert.HasCount(1, fixture.Connection.Commands);
        StringAssert.Contains(fixture.Connection.Text, "user_name_snapshot AS \"UserName\"");
        StringAssert.Contains(fixture.Connection.Text, "ORDER BY occurred_date, uid");
        Assert.AreEqual(aggregateUid, fixture.Connection.Values["AggregateUid"]);
        Assert.IsNull(fixture.Connection.LastTransaction);
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.GetByAggregateAsync("Inventory", aggregateUid, new CancellationToken(true)));
        Assert.HasCount(1, fixture.Connection.Commands);
        fixture.Session.Transaction = fixture.Connection.BeginTransaction();
        await repository.GetByAggregateAsync("Inventory", aggregateUid);
        Assert.HasCount(2, fixture.Connection.Commands);
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
    }

    /// <summary>PG 写入和命令查询没有外层事务时均在发命令前拒绝，不创建或接管事务。</summary>
    [TestMethod]
    public async Task AuditLog_CommandOperationsRequireOuterTransaction()
    {
        using var fixture = new Fixture();
        fixture.Session.Transaction!.Dispose();
        fixture.Session.Transaction = null;
        var repository = AuditLogRepository(fixture);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.GetByModuleCommandAsync("Inventory", Guid.NewGuid()));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.GetByCommandAsync("Inventory", "Post", Guid.NewGuid()));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.InsertAsync(new BusinessOperationLog { CommandId = Guid.NewGuid() }));
        Assert.HasCount(0, fixture.Connection.Commands);
    }

    /// <summary>使用既有夹具的同一会话和审计身份，不复制 ADO 替身。</summary>
    private static PostgreSqlBusinessOperationLogRepository AuditLogRepository(Fixture fixture)
    {
        var user = new User();
        return new(new RepositoryBaseContext(fixture.Session, NullLoggerFactory.Instance, new AuditStampService(user), user));
    }
}
