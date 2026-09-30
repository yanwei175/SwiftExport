using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Systems.ExcelFieldMappings;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Systems.ExcelFieldMappings;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Systems;

/// <summary>在独立的临时 LocalDB 数据库验证整组事务、表头互换及失败回滚。</summary>
[TestClass]
public sealed class ExcelFieldMappingBatchLocalDbTests
{
    /// <summary>仅显式启用时创建随机临时库，测试结束后删除该库。</summary>
    [TestMethod]
    public async Task SaveGroup_IsAtomicAndSupportsHeaderSwap()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_LOCALDB_TESTS") != "1")
            Assert.Inconclusive("设置 SWIFTEXPORT_RUN_LOCALDB_TESTS=1 后运行本机 LocalDB 集成测试。");
        var name = "SwiftExport_FieldMapping_Batch_" + Guid.NewGuid().ToString("N")[..12];
        const string server = "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;Encrypt=False;TrustServerCertificate=True;";
        await using var master = new SqlConnection(server + "Initial Catalog=master");
        await master.OpenAsync();
        var created = false;
        try
        {
            await master.ExecuteAsync($"CREATE DATABASE [{name}]");
            created = true;
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = server + "Initial Catalog=" + name
            }).Build();
            using var session = new DbSession(configuration);
            var user = new TestUser();
            using var logs = LoggerFactory.Create(_ => { });
            var repository = new ExcelFieldMappingRepository(new RepositoryBaseContext(
                session, logs, new AuditStampService(user), user));
            var service = new ExcelFieldMappingBatchService(repository, session,
                logs.CreateLogger<ExcelFieldMappingBatchService>());
            await session.Connection.ExecuteAsync(@"
                SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_WARNINGS ON;
                SET ANSI_PADDING ON; SET CONCAT_NULL_YIELDS_NULL ON;
                SET ARITHABORT ON; SET NUMERIC_ROUNDABORT OFF;
                CREATE TABLE dbo.ExcelFieldMapping (
                    Uid uniqueidentifier NOT NULL PRIMARY KEY, Version int NOT NULL,
                    IsDeleted bit NOT NULL, CreatedDate datetime2 NULL, CreatedBy nvarchar(100) NULL,
                    UpdatedDate datetime2 NULL, UpdatedBy nvarchar(100) NULL,
                    GroupCode nvarchar(40) NOT NULL, ExcelHeader nvarchar(200) NOT NULL,
                    NormalizedHeader nvarchar(200) NOT NULL, TargetFieldKey nvarchar(100) NOT NULL,
                    IsEnabled bit NOT NULL, Remark nvarchar(500) NULL);
                CREATE UNIQUE INDEX UX_ExcelFieldMapping_Group_Header ON dbo.ExcelFieldMapping
                    (GroupCode,NormalizedHeader) WHERE IsDeleted=0 AND NormalizedHeader<>N'';");
            var first = await service.SaveGroupAsync("SalesOrder", new SaveExcelFieldMappingGroupRequest
            {
                Rows = new List<SaveExcelFieldMappingRowRequest>
                {
                    new() { ExcelHeader = "A", TargetFieldKey = "Line.CustomerSku", IsEnabled = true },
                    new() { ExcelHeader = "B", TargetFieldKey = "Header.OrderDate", IsEnabled = true }
                }
            }, default);
            Assert.IsTrue(first.Success, first.Message);
            Assert.HasCount(2, first.Data);
            var original = first.Data.ToArray();
            var duplicate = await service.SaveGroupAsync("SalesOrder", new SaveExcelFieldMappingGroupRequest
            {
                Rows = original.Select(row => new SaveExcelFieldMappingRowRequest
                {
                    Uid = row.Uid, ExpectedVersion = row.Version, ExcelHeader = " a ",
                    TargetFieldKey = row.TargetFieldKey, IsEnabled = true
                }).ToList()
            }, default);
            Assert.IsFalse(duplicate.Success, "规范化后的同组别名不能重复");
            var stale = await service.SaveGroupAsync("SalesOrder", new SaveExcelFieldMappingGroupRequest
            {
                Rows = new List<SaveExcelFieldMappingRowRequest>
                {
                    new() { Uid = original[0].Uid, ExpectedVersion = original[0].Version + 1,
                        ExcelHeader = "OTHER", TargetFieldKey = original[0].TargetFieldKey, IsEnabled = true }
                }
            }, default);
            Assert.IsFalse(stale.Success, "旧版本必须拒绝");
            var swapped = await service.SaveGroupAsync("SalesOrder", new SaveExcelFieldMappingGroupRequest
            {
                Rows = original.Select(row => new SaveExcelFieldMappingRowRequest
                {
                    Uid = row.Uid, ExpectedVersion = row.Version,
                    ExcelHeader = row.ExcelHeader == "A" ? "B" : "A",
                    TargetFieldKey = row.TargetFieldKey, IsEnabled = true
                }).ToList()
            }, default);
            Assert.IsTrue(swapped.Success, swapped.Message);
            Assert.IsTrue(swapped.Data.All(row => row.Version == 2));
            var omitted = await service.SaveGroupAsync("SalesOrder", new SaveExcelFieldMappingGroupRequest
            {
                Rows = new List<SaveExcelFieldMappingRowRequest>
                {
                    new() { Uid = swapped.Data[0].Uid, ExpectedVersion = swapped.Data[0].Version,
                        ExcelHeader = swapped.Data[0].ExcelHeader,
                        TargetFieldKey = swapped.Data[0].TargetFieldKey, IsEnabled = true }
                }
            }, default);
            Assert.IsTrue(omitted.Success);
            Assert.HasCount(2, omitted.Data, "未提交的行不能被整组保存隐式删除");
            Assert.IsTrue(omitted.Data.All(row => row.Version == 2), "未改动行不能抬高版本");
            await session.Connection.ExecuteAsync(@"
                CREATE TRIGGER dbo.FailMappingUpdate ON dbo.ExcelFieldMapping AFTER UPDATE AS
                BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE ExcelHeader=N'FAIL')
                    THROW 53021, 'forced rollback', 1; END;");
            var failed = await service.SaveGroupAsync("SalesOrder", new SaveExcelFieldMappingGroupRequest
            {
                Rows = swapped.Data.Select((row, index) => new SaveExcelFieldMappingRowRequest
                {
                    Uid = row.Uid, ExpectedVersion = row.Version, ExcelHeader = index == 0 ? "CHANGED" : "FAIL",
                    TargetFieldKey = row.TargetFieldKey, IsEnabled = true
                }).ToList()
            }, default);
            Assert.IsFalse(failed.Success);
            var stored = await repository.GetByGroupAsync("SalesOrder", true, default);
            Assert.IsTrue(stored.All(row => row.Version == 2 && row.ExcelHeader != "CHANGED" && row.ExcelHeader != "FAIL"));
        }
        finally
        {
            if (created)
            {
                await master.ExecuteAsync($"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]");
            }
        }
    }

    private sealed class TestUser : ICurrentUser
    {
        public string UserName => "test";
        public Guid? UserId => Guid.Empty;
        public bool IsAuthenticated => true;
    }
}
