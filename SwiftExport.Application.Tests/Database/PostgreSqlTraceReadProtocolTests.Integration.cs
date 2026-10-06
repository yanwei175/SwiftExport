using System.Data.SqlTypes;
using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Application.Tests.Database;

/// <summary>主控在真实PG核验Trace冻结覆盖数组及SQLServer UUID尾键；仅常量读取，不代替仓储完整Trace验收。</summary>
[TestClass]
[DoNotParallelize]
[TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlTraceReadProtocolIntegrationTests
{
    /// <summary>PG UUID规范字节调整后与已实测的SqlGuid顺序一致；用于会改变覆盖归属的窗口并列尾键。</summary>
    [TestMethod]
    public async Task CoverageTieBreaker_PreservesSourceUuidBytePriority()
    {
        using var session = Open(); var values = SqlServerSourceReadProtocolIntegrationTests.SortSamples();
        var parameters = new PostgreSqlParameters(); parameters.AddIds("Ids", values);
        var actual = (await session.Connection.QueryAsync<Guid>("""
            SELECT value FROM unnest(CAST(@Ids AS uuid[])) AS ids(value)
            ORDER BY ARRAY[
                get_byte(uuid_send(value),10),get_byte(uuid_send(value),11),get_byte(uuid_send(value),12),
                get_byte(uuid_send(value),13),get_byte(uuid_send(value),14),get_byte(uuid_send(value),15),
                get_byte(uuid_send(value),8),get_byte(uuid_send(value),9),get_byte(uuid_send(value),7),get_byte(uuid_send(value),6),
                get_byte(uuid_send(value),5),get_byte(uuid_send(value),4),get_byte(uuid_send(value),3),get_byte(uuid_send(value),2),
                get_byte(uuid_send(value),1),get_byte(uuid_send(value),0)]
            """, parameters)).ToArray();
        CollectionAssert.AreEqual(values.OrderBy(id => new SqlGuid(id)).ToArray(), actual);
        Assert.IsNull(session.Transaction);
    }

    /// <summary>等长冻结数组按位置展开，空集合、全未知、重复目标和nullable数值保持配对及SQL聚合含义。</summary>
    [TestMethod]
    [DataRow("empty")]
    [DataRow("null")]
    [DataRow("paired")]
    public async Task FrozenCoverageRelation_BindsTypedArraysAndPreservesUnknownValues(string mode)
    {
        using var session = Open(); var target = Guid.NewGuid(); var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var ids = mode == "empty" ? Array.Empty<Guid>() : new[] { first, second };
        var targets = mode == "empty" ? Array.Empty<Guid>() : new[] { target, target };
        var quantities = mode switch { "empty" => Array.Empty<decimal?>(), "null" => new decimal?[] { null, null }, _ => new decimal?[] { 6m, 1m } };
        var parameters = new PostgreSqlParameters(); parameters.AddIds("LinkIds", ids); parameters.AddIds("TargetIds", targets);
        parameters.Add("Quantities", quantities); parameters.Add("First", first); parameters.Add("Second", second);
        var row = await session.Connection.QuerySingleAsync<CoverageSummary>("""
            WITH frozen AS MATERIALIZED (
                SELECT * FROM unnest(CAST(@LinkIds AS uuid[]),CAST(@TargetIds AS uuid[]),CAST(@Quantities AS numeric[]))
                    AS coverage(link_id,target_line_id,coverage_qty))
            SELECT COUNT(*)::integer AS "Count",COUNT(*) FILTER (WHERE coverage_qty IS NULL)::integer AS "UnknownCount",
                   SUM(coverage_qty) AS "Total",COUNT(DISTINCT target_line_id)::integer AS "TargetCount",
                   (SELECT coverage_qty FROM frozen WHERE link_id=@First) AS "First",
                   (SELECT coverage_qty FROM frozen WHERE link_id=@Second) AS "Second" FROM frozen
            """, parameters);
        Assert.AreEqual(ids.Length, row.Count); Assert.AreEqual(mode == "null" ? 2 : 0, row.UnknownCount);
        Assert.AreEqual(mode == "empty" ? 0 : 1, row.TargetCount);
        Assert.AreEqual(mode == "paired" ? 7m : (decimal?)null, row.Total);
        Assert.AreEqual(mode == "paired" ? 6m : (decimal?)null, row.First);
        Assert.AreEqual(mode == "paired" ? 1m : (decimal?)null, row.Second);
        Assert.IsNull(session.Transaction);
    }

    /// <summary>只借用既有严格故障库门禁，不设置默认凭据、不操作源库/备份或创建数据结构。</summary>
    private static DbSession Open() => new(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));

    /// <summary>局部常量聚合投影，检验NULL区别，不变更公共Trace DTO或业务数量算法。</summary>
    private sealed class CoverageSummary
    {
        public int Count { get; set; }
        public int UnknownCount { get; set; }
        public int TargetCount { get; set; }
        public decimal? Total { get; set; }
        public decimal? First { get; set; }
        public decimal? Second { get; set; }
    }
}
