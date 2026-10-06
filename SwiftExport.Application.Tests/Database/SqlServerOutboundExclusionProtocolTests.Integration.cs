using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SwiftExport.Application.Tests.Database;

/// <summary>只读常量SELECT核验源Dapper实际NOT IN列表展开，避免把null列表猜成NOT IN(NULL)；不读写业务表或创建临时对象。</summary>
[TestClass, DoNotParallelize, TestCategory("SqlServerSourceReadProtocolIntegration")]
public sealed class SqlServerOutboundExclusionProtocolIntegrationTests
{
    /// <summary>与原请求相同的List&lt;Guid&gt;匿名参数，经实际SqlClient/Dapper验证空、null、重复和空身份的过滤。</summary>
    [TestMethod]
    [DataRow("empty", 2)] [DataRow("null", 2)] [DataRow("one", 1)] [DataRow("duplicate", 1)] [DataRow("empty-guid", 2)]
    public async Task ActualListExpansion_PreservesOriginalExcludedRows(string mode, int expected)
    {
        using var connection = Open(); var first = Guid.NewGuid(); var second = Guid.NewGuid();
        List<Guid>? excluded = mode switch
        { "null" => null, "one" => [first], "duplicate" => [first, first], "empty-guid" => [Guid.Empty], _ => [] };
        var result = await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM (VALUES (CAST(@First AS uniqueidentifier)),(CAST(@Second AS uniqueidentifier))) b(ProductId)
            WHERE b.ProductId NOT IN @ExcludedProductIds;
            """, new { First = first, Second = second, ExcludedProductIds = excluded });
        Assert.AreEqual(expected, result);
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT @@TRANCOUNT"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT 1"));
    }

    /// <summary>严格源只读门禁且无默认凭据，只开放已授权SQL Server实例dv_next。</summary>
    private static SqlConnection Open()
    {
        var value = Environment.GetEnvironmentVariable("SWIFTEXPORT_SQL_SOURCE_READ_CONNECTION");
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_SQL_SOURCE_READ_TESTS") != "1" || string.IsNullOrWhiteSpace(value))
            Assert.Inconclusive("未显式启用源SQL Server只读列表探针，本项未运行。");
        SqlConnectionStringBuilder settings;
        try { settings = new(value); } catch (ArgumentException) { Assert.Fail("源只读连接格式无效。"); throw; }
        Assert.AreEqual("192.168.3.12", settings.DataSource); Assert.AreEqual("dv_next", settings.InitialCatalog.ToLowerInvariant());
        settings.Pooling = false; settings.ConnectTimeout = 10;
        var connection = new SqlConnection(settings.ConnectionString); connection.Open(); return connection;
    }
}
