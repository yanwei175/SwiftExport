using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Core.Entities.Systems.ExcelReadProfiles;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Systems.ExcelReadProfiles;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>使用真实 Npgsql、Dapper 和 PG 仓储验收配置聚合；直接编排事务，不代表 Application 或 API 完整验收。</summary>
[TestClass]
[TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlExcelProfileIntegrationTests
{
    /// <summary>六方法真实读写保留身份、投影和版本；字段错误归属造成中途异常后回滚，另一连接确认头行均无残留。</summary>
    [TestMethod]
    [Timeout(60000)]
    public async Task ExcelProfile_LifecycleAndFailedFieldRollbackLeaveNoRows()
    {
        var connectionString = PostgreSqlIntegrationEnvironment.GetConnectionString();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = timeout.Token;
        var head = NewHead();
        var field = NewField(head.Uid);
        using var session = OpenSession(connectionString);
        var repository = Repository(session);
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        try
        {
            await AssertNoCollision(session, head, field, token);
            var originalUid = head.Uid;
            await repository.InsertAsync(head, token);
            await repository.SaveFieldAsync(field, true, token);
            Assert.AreEqual(originalUid, head.Uid);
            var list = await repository.ListAsync(head.GroupCode, token);
            Assert.HasCount(1, list);
            Assert.AreEqual(head.Uid, list.Single().Uid);
            Assert.AreEqual(head.ConfigName, list.Single().ConfigName);
            var loaded = await repository.FindAsync(head.Uid, true, token);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(1, loaded.Version);
            Assert.AreEqual(1, loaded.EndMode);
            Assert.AreEqual("A", loaded.LastRowColumn);
            Assert.IsNull(loaded.SummaryCellAddress);
            Assert.AreEqual("__pgtest_excel", loaded.CreatedBy);
            Assert.IsNotNull(loaded.CreatedDate);
            var loadedField = (await repository.GetFieldsAsync(head.Uid, token)).Single();
            Assert.AreEqual(field.Uid, loadedField.Uid);
            Assert.AreEqual(head.Uid, loadedField.ProfileUid);
            Assert.AreEqual("Line.Sku", loadedField.TargetFieldKey);
            Assert.AreEqual("A8", loadedField.CellAddress);
            Assert.IsTrue(loadedField.IsDownward);
            Assert.IsNull(loadedField.WorksheetName);
            loaded.Remark = "本次变更必须回滚";
            Assert.IsTrue(await repository.UpdateAsync(loaded, 1, token));
            Assert.AreEqual(2, loaded.Version);
            loaded.Remark = "旧版本不得覆盖";
            Assert.IsFalse(await repository.UpdateAsync(loaded, 1, token));
            Assert.AreEqual(2, loaded.Version);
            Assert.AreEqual("本次变更必须回滚", (await repository.FindAsync(head.Uid, false, token))!.Remark);
            loadedField.CellAddress = "B8";
            await repository.SaveFieldAsync(loadedField, false, token);
            Assert.AreEqual("B8", (await repository.GetFieldsAsync(head.Uid, token)).Single().CellAddress);
            loadedField.ProfileUid = Guid.NewGuid();
            loadedField.CellAddress = "C8";
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.SaveFieldAsync(loadedField, false, token));
            Assert.AreEqual("B8", (await repository.GetFieldsAsync(head.Uid, token)).Single().CellAddress);
        }
        finally
        {
            // 本场景不提交任何数据，应用层应承担的整份回滚由测试显式执行。
            transaction.Rollback();
            session.Transaction = null;
            using var verification = OpenSession(connectionString);
            await AssertAbsent(verification, head.Uid, field.Uid);
        }
    }

    /// <summary>首连接锁主配置并更新后，确认第二连接真实等待该锁；提交后旧主版本只允许一方成功，字段不可被失败方覆盖。</summary>
    [TestMethod]
    [Timeout(90000)]
    public async Task ExcelProfile_TwoConnectionsCompeteForSameHeadVersion()
    {
        var connectionString = PostgreSqlIntegrationEnvironment.GetConnectionString();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = timeout.Token;
        var head = NewHead();
        var field = NewField(head.Uid);
        var seeded = false;
        try
        {
            using var first = OpenSession(connectionString);
            using var second = OpenSession(connectionString);
            using var observer = OpenSession(connectionString);
            await SeedCommitted(first, head, field, token);
            seeded = true;
            var firstPid = await first.Connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT pg_backend_pid()", cancellationToken: token));
            var secondPid = await second.Connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT pg_backend_pid()", cancellationToken: token));
            Assert.AreNotEqual(firstPid, secondPid);
            var firstRepository = Repository(first);
            using var firstTransaction = first.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
            first.Transaction = firstTransaction;
            var committed = false;
            Task<bool>? competing = null;
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                var firstHead = (await firstRepository.FindAsync(head.Uid, true, token))!;
                firstHead.Remark = "首连接胜出";
                Assert.IsTrue(await firstRepository.UpdateAsync(firstHead, 1, token));
                var firstField = (await firstRepository.GetFieldsAsync(head.Uid, token)).Single();
                firstField.CellAddress = "B8";
                await firstRepository.SaveFieldAsync(firstField, false, token);
                competing = Compete();
                await started.Task.WaitAsync(token);
                await WaitForBlocking(observer, secondPid, firstPid, token);
                Assert.IsFalse(competing.IsCompleted, "第二连接必须真实等待尚未释放的主配置锁。");
                firstTransaction.Commit();
                committed = true;
                first.Transaction = null;
                Assert.IsFalse(await competing.WaitAsync(token), "旧主版本不得成功覆盖首连接结果。");
                var finalHead = (await Repository(observer).FindAsync(head.Uid, false, token))!;
                Assert.AreEqual(2, finalHead.Version);
                Assert.AreEqual("首连接胜出", finalHead.Remark);
                Assert.AreEqual("B8", (await Repository(observer).GetFieldsAsync(head.Uid, token)).Single().CellAddress);
            }
            finally
            {
                // 断言或等待失败时先释放首连接锁，再收束第二连接任务，避免清理与未结束写入竞争。
                if (!committed) firstTransaction.Rollback();
                first.Transaction = null;
                if (competing is not null) await competing;
            }

            // 第二连接沿用应用用例的先锁主配置顺序；只在主版本成功时写字段。
            async Task<bool> Compete()
            {
                using var transaction = second.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
                second.Transaction = transaction;
                try
                {
                    var repository = Repository(second);
                    started.TrySetResult(true);
                    var secondHead = (await repository.FindAsync(head.Uid, true, token))!;
                    secondHead.Remark = "旧版本不应胜出";
                    var succeeded = await repository.UpdateAsync(secondHead, 1, token);
                    if (succeeded)
                    {
                        var secondField = (await repository.GetFieldsAsync(head.Uid, token)).Single();
                        secondField.CellAddress = "C8";
                        await repository.SaveFieldAsync(secondField, false, token);
                    }
                    if (succeeded) transaction.Commit();
                    else transaction.Rollback();
                    return succeeded;
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
                finally { second.Transaction = null; }
            }
        }
        finally
        {
            if (seeded) await CleanupCommitted(connectionString, head, field);
        }
    }

    /// <summary>读连接先建立 RepeatableRead 主快照，再由另一连接提交头行更新；原事务仍读旧头旧字段，新事务读新头新字段。</summary>
    [TestMethod]
    [Timeout(90000)]
    public async Task ExcelProfile_RepeatableReadKeepsHeadAndFieldsInOneSnapshot()
    {
        var connectionString = PostgreSqlIntegrationEnvironment.GetConnectionString();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = timeout.Token;
        var head = NewHead();
        var field = NewField(head.Uid);
        var seeded = false;
        try
        {
            using var reader = OpenSession(connectionString);
            using var writer = OpenSession(connectionString);
            await SeedCommitted(writer, head, field, token);
            seeded = true;
            var headRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var writeCommitted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var readTransaction = reader.Connection.BeginTransaction(IsolationLevel.RepeatableRead);
            reader.Transaction = readTransaction;
            var writerTask = WriteNewSnapshot();
            try
            {
                var repository = Repository(reader);
                var original = (await repository.FindAsync(head.Uid, false, token))!;
                Assert.AreEqual(1, original.Version);
                headRead.TrySetResult(true);
                await writeCommitted.Task.WaitAsync(token);
                var sameHead = (await repository.FindAsync(head.Uid, false, token))!;
                var oldField = (await repository.GetFieldsAsync(head.Uid, token)).Single();
                Assert.AreEqual(1, sameHead.Version);
                Assert.IsNull(sameHead.Remark);
                Assert.AreEqual("A8", oldField.CellAddress);
                readTransaction.Commit();
                reader.Transaction = null;
            }
            catch
            {
                timeout.Cancel();
                readTransaction.Rollback();
                throw;
            }
            finally
            {
                reader.Transaction = null;
                await writerTask;
            }
            var newHead = (await Repository(reader).FindAsync(head.Uid, false, token))!;
            Assert.AreEqual(2, newHead.Version);
            Assert.AreEqual("新快照", newHead.Remark);
            Assert.AreEqual("B8", (await Repository(reader).GetFieldsAsync(head.Uid, token)).Single().CellAddress);

            // 写提交信号只在真实事务提交后发布，读断言不能以计时推测提交顺序。
            async Task WriteNewSnapshot()
            {
                await headRead.Task.WaitAsync(token);
                using var transaction = writer.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
                writer.Transaction = transaction;
                try
                {
                    var repository = Repository(writer);
                    var current = (await repository.FindAsync(head.Uid, true, token))!;
                    current.Remark = "新快照";
                    Assert.IsTrue(await repository.UpdateAsync(current, 1, token));
                    var currentField = (await repository.GetFieldsAsync(head.Uid, token)).Single();
                    currentField.CellAddress = "B8";
                    await repository.SaveFieldAsync(currentField, false, token);
                    transaction.Commit();
                    writeCommitted.TrySetResult(true);
                }
                catch (Exception exception)
                {
                    transaction.Rollback();
                    writeCommitted.TrySetException(exception);
                    throw;
                }
                finally { writer.Transaction = null; }
            }
        }
        finally
        {
            if (seeded) await CleanupCommitted(connectionString, head, field);
        }
    }

    /// <summary>创建已校验目标的真实会话，不读取默认配置或输出连接材料。</summary>
    private static DbSession OpenSession(string connectionString) => new(new PostgreSqlConnectionFactory(connectionString));

    /// <summary>使真实仓储与审计服务共享测试会话及调用方事务。</summary>
    private static PostgreSqlExcelReadProfileRepository Repository(DbSession session)
    {
        var user = new IntegrationUser();
        return new(new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user));
    }

    /// <summary>每场景使用独立随机主键、分组及测试前缀名称，避免引用或覆盖已有配置。</summary>
    private static ExcelReadProfile NewHead()
    {
        var uid = Guid.NewGuid();
        return new() { Uid = uid, GroupCode = "__pgtest_" + uid.ToString("N")[..24], ConfigName = "__pgtest_" + uid.ToString("N"), EndMode = 1, LastRowColumn = "A" };
    }

    /// <summary>生成随机字段身份，逻辑关系仅指向本场景主配置。</summary>
    private static ExcelReadField NewField(Guid profileUid) => new()
        { Uid = Guid.NewGuid(), ProfileUid = profileUid, TargetFieldKey = "Line.Sku", CellAddress = "A8", IsDownward = true };

    /// <summary>提交跨连接测试所需头行前检查标识及配置键无碰撞；失败时回滚，不对现存记录执行更新。</summary>
    private static async Task SeedCommitted(DbSession session, ExcelReadProfile head, ExcelReadField field, CancellationToken token)
    {
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        try
        {
            await AssertNoCollision(session, head, field, token);
            var repository = Repository(session);
            await repository.InsertAsync(head, token);
            await repository.SaveFieldAsync(field, true, token);
            transaction.Commit();
        }
        catch { transaction.Rollback(); throw; }
        finally { session.Transaction = null; }
    }

    /// <summary>核对本次头行 Uid 和随机分组键尚不存在；不把空库或测试库名称当作清理授权。</summary>
    private static async Task AssertNoCollision(DbSession session, ExcelReadProfile head, ExcelReadField field, CancellationToken token)
    {
        Assert.AreEqual(0L, await session.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM public.excel_read_profile WHERE uid=@Uid OR (group_code=@GroupCode AND config_name=@ConfigName)",
            new { head.Uid, head.GroupCode, head.ConfigName }, session.Transaction, cancellationToken: token)));
        Assert.AreEqual(0L, await session.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM public.excel_read_field WHERE uid=@Uid OR profile_uid=@ProfileUid",
            new { field.Uid, field.ProfileUid }, session.Transaction, cancellationToken: token)));
    }

    /// <summary>通过服务器报告的阻塞 PID 确认真实锁等待；每次查询受超时取消控制，不用 sleep 推测竞争。</summary>
    private static async Task WaitForBlocking(DbSession observer, int waitingPid, int holdingPid, CancellationToken token)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
        wait.CancelAfter(TimeSpan.FromSeconds(15));
        while (!await observer.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT @HoldingPid = ANY(pg_blocking_pids(@WaitingPid))", new { HoldingPid = holdingPid, WaitingPid = waitingPid }, cancellationToken: wait.Token)))
            wait.Token.ThrowIfCancellationRequested();
    }

    /// <summary>只按本次明确字段和主配置 Uid 反向清理，并附加关系和随机分组保护；最后确认无残留。</summary>
    private static async Task CleanupCommitted(string connectionString, ExcelReadProfile head, ExcelReadField field)
    {
        using var cleanup = OpenSession(connectionString);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var transaction = cleanup.Connection.BeginTransaction();
        cleanup.Transaction = transaction;
        try
        {
            await cleanup.Connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM public.excel_read_field WHERE uid=@Uid AND profile_uid=@ProfileUid", new { field.Uid, field.ProfileUid }, transaction, cancellationToken: timeout.Token));
            await cleanup.Connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM public.excel_read_profile WHERE uid=@Uid AND group_code=@GroupCode AND config_name=@ConfigName", new { head.Uid, head.GroupCode, head.ConfigName }, transaction, cancellationToken: timeout.Token));
            transaction.Commit();
        }
        catch { transaction.Rollback(); throw; }
        finally { cleanup.Transaction = null; }
        await AssertAbsent(cleanup, head.Uid, field.Uid);
    }

    /// <summary>在回滚或精确清理之后查询本次明确身份，头行任一残留均使测试失败。</summary>
    private static async Task AssertAbsent(DbSession session, Guid profileUid, Guid fieldUid)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.AreEqual(0L, await session.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM public.excel_read_profile WHERE uid=@ProfileUid", new { ProfileUid = profileUid }, cancellationToken: timeout.Token)));
        Assert.AreEqual(0L, await session.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM public.excel_read_field WHERE uid=@FieldUid OR profile_uid=@ProfileUid", new { FieldUid = fieldUid, ProfileUid = profileUid }, cancellationToken: timeout.Token)));
    }

    /// <summary>仅为本次配置审计提供测试身份，不创建用户或授予权限。</summary>
    private sealed class IntegrationUser : ICurrentUser
    {
        /// <summary>测试前缀审计名称。</summary>
        public string UserName => "__pgtest_excel";
        /// <summary>没有对应持久化账号。</summary>
        public Guid? UserId => null;
        /// <summary>仅满足审计上下文，不代表 API 已认证。</summary>
        public bool IsAuthenticated => true;
    }
}
