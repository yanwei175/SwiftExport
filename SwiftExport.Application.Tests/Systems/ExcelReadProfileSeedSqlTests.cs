using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SwiftExport.Application.Tests.Systems;

/// <summary>在随机临时库验证本次明确授权的双表增量脚本，不触碰业务数据库。</summary>
[TestClass]
public sealed class ExcelReadProfileSeedSqlTests
{
    /// <summary>脚本能首次建表、重复执行，保留配置改动和软删除；不兼容结构必须拒绝。</summary>
    [TestMethod]
    public async Task Script_CreatesAndSeedsWithoutOverwriting()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_LOCALDB_TESTS") != "1")
            Assert.Inconclusive("设置 SWIFTEXPORT_RUN_LOCALDB_TESTS=1 后运行隔离 SQL 脚本测试。");
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SwiftExport.slnx"))) directory = directory.Parent;
        Assert.IsNotNull(directory);
        var script = File.ReadAllText(Path.Combine(directory.FullName, "SwiftExport.Infrastructure", "Database", "Excel单据地址读取配置建表与初始化.sql"));
        const string server = "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;Encrypt=False;TrustServerCertificate=True;";
        var name = "SwiftExport_ExcelReadSeed_" + Guid.NewGuid().ToString("N");
        await using var master = new SqlConnection(server + "Initial Catalog=master");
        await master.OpenAsync();
        var created = false;
        try
        {
            await master.ExecuteAsync($"CREATE DATABASE [{name}]");
            created = true;
            await using var connection = new SqlConnection(server + "Initial Catalog=" + name);
            await connection.OpenAsync();
            await connection.ExecuteAsync(script);
            var ids = (await connection.QueryAsync<Guid>("SELECT Uid FROM dbo.ExcelReadProfile ORDER BY GroupCode")).ToArray();
            Assert.HasCount(2, ids);
            Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.ExcelReadField"));
            Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.ExcelReadProfile WHERE Version=1 AND EndMode=0 AND IsDeleted=0"));
            Assert.AreEqual(27, await connection.ExecuteScalarAsync<int>(@"SELECT COUNT(*) FROM sys.extended_properties
                WHERE class=1 AND name=N'MS_Description' AND major_id IN (OBJECT_ID(N'dbo.ExcelReadProfile'),OBJECT_ID(N'dbo.ExcelReadField'))"));
            await connection.ExecuteAsync(script);
            CollectionAssert.AreEqual(ids, (await connection.QueryAsync<Guid>("SELECT Uid FROM dbo.ExcelReadProfile ORDER BY GroupCode")).ToArray());

            // 使用者的边界设置、版本与删除不能被重复初始化覆盖。
            await connection.ExecuteAsync(@"UPDATE dbo.ExcelReadProfile SET Version=7,EndMode=1,LastRowColumn=N'A',Remark=N'已编辑' WHERE GroupCode=N'SalesOrder';
                UPDATE dbo.ExcelReadProfile SET IsDeleted=1 WHERE GroupCode=N'PurchaseOrder';");
            await connection.ExecuteAsync(script);
            Assert.AreEqual(2, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.ExcelReadProfile"));
            Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.ExcelReadProfile WHERE GroupCode=N'SalesOrder' AND Version=7 AND EndMode=1 AND Remark=N'已编辑'"));
            Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.ExcelReadProfile WHERE GroupCode=N'PurchaseOrder' AND IsDeleted=1"));
            Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.foreign_keys"));

            await connection.ExecuteAsync("ALTER TABLE dbo.ExcelReadProfile ALTER COLUMN LastRowColumn nvarchar(5) NULL");
            try
            {
                await connection.ExecuteAsync(script);
                Assert.Fail("不兼容列长度必须拒绝执行");
            }
            catch (SqlException exception) { Assert.AreEqual(53103, exception.Number); }
            Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT @@TRANCOUNT"));
        }
        finally
        {
            // 数据库名称由本测试固定前缀与 Guid 生成，只清理本次实际创建的临时库。
            if (created) await master.ExecuteAsync($"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]");
        }
    }
}
