using System.Data;
using System.Data.SqlTypes;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SwiftExport.Application.Tests.Database;

/// <summary>仅以常量SELECT、等待及显式探针错误核验源驱动读取边界，不创建临时表或修改任何业务数据/结构。</summary>
[TestClass]
[DoNotParallelize]
[TestCategory("SqlServerSourceReadProtocolIntegration")]
public sealed class SqlServerSourceReadProtocolIntegrationTests
{
    /// <summary>空头读取仍推进下一结果，下一结果之前的技术错误不能被空头分支吞掉。</summary>
    [TestMethod]
    public async Task EmptyHeader_ObservesErrorBeforeNextResultMetadata()
    {
        using var connection = Open();
        var error = await Assert.ThrowsAsync<SqlException>(() => ReadEmptyHeader(connection,
            "SELECT CAST(NULL AS int) AS Value WHERE 1=0; THROW 51041,N'迁移只读下一结果探针',1;"));
        Assert.AreEqual(51041, error.Number);
        await Reusable(connection);
    }

    /// <summary>第二结果已有元数据后空头同步Dispose取消下游等待，不能假定尾部所有查询都执行。</summary>
    [TestMethod]
    public async Task EmptyHeader_CancelsUnreadTailAfterNextResultMetadata()
    {
        using var connection = Open();
        var header = await ReadEmptyHeader(connection,
            "SELECT CAST(NULL AS int) AS Value WHERE 1=0; SELECT 41 AS Value; WAITFOR DELAY '00:00:02'; THROW 51042,N'迁移只读未读尾部探针',1;");
        Assert.IsNull(header);
        await Reusable(connection);
    }

    /// <summary>源历史THROW在默认XACT_ABORT关闭的外层事务保持可回滚状态，记录业务异常与服务器事务影响边界。</summary>
    [TestMethod]
    public async Task SourceHistoryThrow_KeepsCallerTransactionUsableWithOriginalSessionDefaults()
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT CASE WHEN (16384 & @@OPTIONS)=16384 THEN 1 ELSE 0 END", transaction: transaction));
            var error = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("THROW 51001,N'迁移只读历史分类探针',1", transaction: transaction));
            Assert.AreEqual(51001, error.Number);
            Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT XACT_STATE()", transaction: transaction));
        }
        finally { transaction.Rollback(); }
        await Reusable(connection);
    }

    /// <summary>固定跨字段及随机UUID由真实源ORDER BY排序，与SqlGuid比对，为覆盖量的并列尾键提供事实证据。</summary>
    [TestMethod]
    public async Task SourceUuidOrdering_MatchesSqlGuidAcrossEveryByte()
    {
        using var connection = Open();
        var values = SortSamples();
        var expressions = string.Join(",", values.Select((_, index) => $"(@Id{index})"));
        var parameters = new DynamicParameters(); for (var i = 0; i < values.Count; i++) parameters.Add("Id" + i, values[i]);
        var actual = (await connection.QueryAsync<Guid>($"SELECT value FROM (VALUES {expressions}) AS ids(value) ORDER BY value", parameters)).ToArray();
        CollectionAssert.AreEqual(values.OrderBy(id => new SqlGuid(id)).ToArray(), actual);
        await Reusable(connection);
    }

    /// <summary>构造每个规范字节位置与多种跨字节权重，避免仅测正常UUID或恰好同序样例。</summary>
    internal static List<Guid> SortSamples()
    {
        var values = new List<Guid> { Guid.Empty, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff") };
        for (var i = 0; i < 16; i++)
        {
            var bytes = new byte[16]; bytes[i] = 1; values.Add(new Guid(bytes)); bytes[i] = 128; values.Add(new Guid(bytes));
        }
        for (var i = 0; i < 64; i++) values.Add(Guid.NewGuid());
        return values;
    }

    /// <summary>保持源C#的同步using及空头分支，不用其他驱动或DisposeAsync代替待核验调用。</summary>
    private static async Task<Row?> ReadEmptyHeader(SqlConnection connection, string sql)
    {
        using var multi = await connection.QueryMultipleAsync(sql);
        var row = await multi.ReadSingleOrDefaultAsync<Row>();
        if (row is null) return null;
        return row;
    }

    /// <summary>只允许明确启用的源实例dv_next，只读SQL不从PG凭据推测SQLServer口令。</summary>
    private static SqlConnection Open()
    {
        var value = Environment.GetEnvironmentVariable("SWIFTEXPORT_SQL_SOURCE_READ_CONNECTION");
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_SQL_SOURCE_READ_TESTS") != "1" || string.IsNullOrWhiteSpace(value))
            Assert.Inconclusive("未显式启用源SQLServer只读协议探针，本项未运行。");
        SqlConnectionStringBuilder settings;
        try { settings = new(value); } catch (ArgumentException) { Assert.Fail("源只读连接格式无效。"); throw; }
        Assert.AreEqual("192.168.3.12", settings.DataSource); Assert.AreEqual("dv_next", settings.InitialCatalog.ToLowerInvariant());
        settings.ConnectTimeout = 10; settings.Pooling = false;
        var connection = new SqlConnection(settings.ConnectionString); connection.Open(); return connection;
    }

    /// <summary>每个探针后连接可查询且无外层事务；不清理表或回退现有成果。</summary>
    private static async Task Reusable(SqlConnection connection)
    {
        Assert.AreEqual(ConnectionState.Open, connection.State);
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT @@TRANCOUNT"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT 1"));
    }

    /// <summary>只承接常量空头，不读取或暴露任何业务行。</summary>
    private sealed class Row { public int Value { get; set; } }
}
