using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Systems.ExcelReadProfiles;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Systems.ExcelReadProfiles;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Systems;

/// <summary>在随机临时 LocalDB 空库运行真实生成基线，验证地址配置的约束、版本、事务和清理。</summary>
[TestClass]
public sealed class ExcelReadProfileLocalDbTests
{
    /// <summary>覆盖创建、分步保存、清除字段、并发保护、失败回滚和整份软删除；不连接开发业务库。</summary>
    [TestMethod]
    public async Task ProfileLifecycle_IsAtomicAndVersioned()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_LOCALDB_TESTS") != "1")
            Assert.Inconclusive("设置 SWIFTEXPORT_RUN_LOCALDB_TESTS=1 后运行随机临时 LocalDB 测试。");
        const string server = "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;Encrypt=False;TrustServerCertificate=True;";
        var database = "SwiftExport_ExcelRead_" + Guid.NewGuid().ToString("N");
        await using var master = new SqlConnection(server + "Initial Catalog=master");
        await master.OpenAsync();
        var created = false;
        try
        {
            await master.ExecuteAsync($"CREATE DATABASE [{database}]");
            created = true;
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:Default"] = server + "Initial Catalog=" + database }).Build();
            using var session = new DbSession(configuration);
            using var logs = LoggerFactory.Create(_ => { });
            // 使用真实实体生成器，数据库列、CHECK、索引和中文描述与交付基线保持相同。
            await session.Connection.ExecuteAsync(new SqlServerSchemaGenerator().Generate(typeof(BaseEntity).Assembly), commandTimeout: 120);
            Assert.AreEqual(0, await session.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.foreign_keys"));
            var user = new TestUser();
            var repository = new ExcelReadProfileRepository(new RepositoryBaseContext(session, logs, new AuditStampService(user), user));
            var service = new ExcelReadProfileService(repository, new ServiceBaseContext(session, logs));
            var create = await service.CreateAsync(new CreateExcelReadProfileRequest { GroupCode = "PurchaseOrder", ConfigName = " 客户 A " }, default);
            Assert.IsTrue(create.Success, create.Message);
            var uid = create.Data.Uid;
            Assert.AreEqual(1, create.Data.Version);
            Assert.AreEqual("客户 A", create.Data.ConfigName);
            Assert.AreEqual("test", create.Data.CreatedBy);
            Assert.IsFalse(create.Data.CanRead);
            Assert.IsNull(session.Transaction);
            var duplicate = await service.CreateAsync(new CreateExcelReadProfileRequest { GroupCode = "PurchaseOrder", ConfigName = "客户 A" }, default);
            Assert.AreEqual("EXCEL_READ_DUPLICATE_NAME", duplicate.Error.Code);
            Assert.IsNull(session.Transaction);
            Assert.IsTrue((await service.CreateAsync(new CreateExcelReadProfileRequest { GroupCode = "SalesOrder", ConfigName = "客户 A" }, default)).Success);

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
                "SELECT COUNT(*) FROM dbo.ExcelReadField WHERE ProfileUid=@Uid AND IsDeleted=0 AND WorksheetName IS NULL", new { Uid = uid }));
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
            Assert.AreEqual(1, await session.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.ExcelReadField WHERE ProfileUid=@Uid AND IsDeleted=1", new { Uid = uid }));

            // 后半段字段写入失败时，先写入的主版本、配置名和所有字段都必须回滚。
            await session.Connection.ExecuteAsync(@"CREATE TRIGGER dbo.FailExcelReadField ON dbo.ExcelReadField AFTER UPDATE AS
                BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE WorksheetName=N'FAIL') THROW 53021,'forced rollback',1; END;");
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
            await session.Connection.ExecuteAsync("DROP TRIGGER dbo.FailExcelReadField");

            // 两个请求使用同一个主版本时只允许一个成功，另一个不得覆盖字段。
            using (var otherSession = new DbSession(configuration))
            {
                var otherRepo = new ExcelReadProfileRepository(new RepositoryBaseContext(otherSession, logs, new AuditStampService(user), user));
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
            await session.Connection.ExecuteAsync(@"CREATE TRIGGER dbo.SlowExcelReadField ON dbo.ExcelReadField AFTER UPDATE AS
                BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE WorksheetName=N'SLOW') WAITFOR DELAY '00:00:05'; END;");
            var cancelRequest = Request(5, [Field("Line.Sku", "C8")]);
            cancelRequest.EndMode = 1;
            cancelRequest.LastRowColumn = "C";
            cancelRequest.Fields[0].WorksheetName = "SLOW";
            using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
            {
                var wasCancelled = false;
                try { await service.UpdateAsync(uid, cancelRequest, cancellation.Token); }
                catch (OperationCanceledException) { wasCancelled = true; }
                Assert.IsTrue(wasCancelled, "SQL 请求取消必须透传给调用方");
            }
            Assert.IsNull(session.Transaction);
            var afterCancellation = (await service.GetAsync(uid, default)).Data;
            Assert.AreEqual(5, afterCancellation.Version);
            Assert.AreEqual(beforeDelete.ConfigName, afterCancellation.ConfigName);
            Assert.AreEqual(beforeDelete.Fields[0].CellAddress, afterCancellation.Fields[0].CellAddress);
            Assert.AreEqual("订单", afterCancellation.Fields[0].WorksheetName);
            await session.Connection.ExecuteAsync("DROP TRIGGER dbo.SlowExcelReadField");
            Assert.IsTrue((await service.DeleteAsync(uid, 5, default)).Success);
            Assert.AreEqual(ServiceErrorCodes.NotFound, (await service.GetAsync(uid, default)).Error.Code);
            Assert.HasCount(0, (await service.ListAsync("PurchaseOrder", default)).Data);
            Assert.HasCount(0, await repository.GetFieldsAsync(uid, default));
            Assert.AreEqual(6, await session.Connection.ExecuteScalarAsync<int>("SELECT Version FROM dbo.ExcelReadProfile WHERE Uid=@Uid", new { Uid = uid }));
            Assert.IsTrue((await service.CreateAsync(new CreateExcelReadProfileRequest { GroupCode = "PurchaseOrder", ConfigName = "客户 A" }, default)).Success);
            Assert.IsNull(session.Transaction);
        }
        finally
        {
            // 名称仅由固定前缀与 Guid 产生，只清理本次确实创建的随机临时库。
            if (created) await master.ExecuteAsync($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]");
        }
    }

    private static UpdateExcelReadProfileRequest Request(int version, List<ExcelReadFieldDto> fields) => new()
        { ExpectedVersion = version, ConfigName = "客户 A", Fields = fields };
    private static ExcelReadFieldDto Field(string key, string address) => new()
        { TargetFieldKey = key, WorksheetName = "订单", CellAddress = address, IsDownward = true };

    /// <summary>仅为临时库审计提供身份，不模拟权限授予。</summary>
    private sealed class TestUser : ICurrentUser
    {
        public string UserName => "test";
        public Guid? UserId => Guid.Empty;
        public bool IsAuthenticated => true;
    }
}
