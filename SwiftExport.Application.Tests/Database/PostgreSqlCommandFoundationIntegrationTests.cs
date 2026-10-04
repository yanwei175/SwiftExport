using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Commands;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Auditing;
using SwiftExport.Infrastructure.Repositories.Commands;
using SwiftExport.Infrastructure.Repositories.Systems.Numbering;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>验证取号、命令回执和业务审计共享真实连接事务，失败回滚不留下任何部分事实。</summary>
[TestClass]
[TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlCommandFoundationIntegrationTests
{
    /// <summary>三个仓储在同一事务内可见，另一连接不可见；外层失败回滚后计数、回执和审计同时消失。</summary>
    [TestMethod]
    [Timeout(60000)]
    public async Task CommandFoundation_SharedTransactionRollsBackAllThreeFacts()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetConnectionString();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = timeout.Token;
        using var session = new DbSession(new PostgreSqlConnectionFactory(connection));
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        var user = new TestUser();
        var context = new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
        var receipts = new PostgreSqlCommandReceiptRepository(context);
        var logs = new PostgreSqlBusinessOperationLogRepository(context);
        var numbers = new PostgreSqlDocumentSequenceRepository(context);
        var commandId = Guid.NewGuid();
        var docType = "__pgtest_" + Guid.NewGuid().ToString("N")[..12];
        var receipt = new CommandReceipt
        {
            CommandId = commandId, ActorUid = user.UserId!.Value, CommandType = "__pgtest_foundation",
            RequestFingerprint = new string('A', 64), ResultJson = "{\"ok\":true}", RecordedAtUtc = DateTime.UtcNow
        };
        var log = new BusinessOperationLog
        {
            CommandId = commandId, Module = "__pgtest_foundation", AggregateType = "Test",
            AggregateUid = Guid.NewGuid(), OperationType = "Rollback", OperationName = "事务回滚验证",
            CorrelationId = Guid.NewGuid().ToString("N"), UserUid = user.UserId,
            UserNameSnapshot = user.UserName!, OccurredDate = DateTime.Now
        };
        var keys = new { CommandId = commandId, DocType = docType };
        const string countSql = """
            SELECT (SELECT count(*) FROM public.document_sequence WHERE doc_type=@DocType)
                 + (SELECT count(*) FROM public.command_receipt WHERE command_id=@CommandId)
                 + (SELECT count(*) FROM public.business_operation_log WHERE command_id=@CommandId);
            """;
        Assert.AreEqual(0L, await observer.Connection.ExecuteScalarAsync<long>(new CommandDefinition(countSql, keys, cancellationToken: token)));
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        try
        {
            Assert.AreEqual(0, await receipts.LockAsync(commandId, token));
            Assert.AreEqual(1, (await numbers.GetNextAsync(docType, "202610", user.UserName)).CurrentNo);
            await receipts.InsertAsync(receipt, token);
            Assert.AreEqual(1, await logs.InsertAsync(log));
            Assert.AreEqual(receipt.Uid, (await receipts.FindAsync(commandId, token))!.Uid);
            Assert.AreEqual(log.Uid, (await logs.GetByModuleCommandAsync(log.Module, commandId))!.Uid);
            Assert.AreEqual(3L, await session.Connection.ExecuteScalarAsync<long>(new CommandDefinition(countSql, keys, transaction, cancellationToken: token)));
            Assert.AreEqual(0L, await observer.Connection.ExecuteScalarAsync<long>(new CommandDefinition(countSql, keys, cancellationToken: token)));
        }
        finally
        {
            // 不提交不可变回执和审计，调用方一次回滚同时清理三个仓储的本次写入。
            transaction.Rollback();
            session.Transaction = null;
        }
        Assert.AreEqual(0L, await observer.Connection.ExecuteScalarAsync<long>(new CommandDefinition(countSql, keys, cancellationToken: token)));
    }

    /// <summary>只提供本次随机测试身份，不借用或修改现有用户数据。</summary>
    private sealed class TestUser : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public string UserName => "__pgtest_foundation";
        public bool IsAuthenticated => true;
    }
}
