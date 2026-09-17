using System.Reflection;
using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Infrastructure.Repositories.Shipping;

namespace SwiftExport.Application.Tests.Shipping;

/// <summary>不连接数据库，验证 Picker 专用查询的参数隔离、准入和分页边界。</summary>
[TestClass]
public sealed class PackingListSourceQueryTests
{
    /// <summary>批量词按 OR 参数化，Target 采用回退阻断的未释放数量口径。</summary>
    [TestMethod]
    public void BatchKeywordsAndTargetAreFilteredBeforePaging()
    {
        var parameters = new DynamicParameters();
        var method = typeof(PackingListRepository).GetMethod("BuildSourceSearchSql", BindingFlags.NonPublic | BindingFlags.Static)!;
        var sql = (string)method.Invoke(null, [new PackingListSourceSearchRequest
        { Keywords = [" A% ", "a%", "B'"], Keyword = "A%" }, parameters])!;
        Assert.AreEqual("%A~%%", parameters.Get<string>("Keyword0"));
        Assert.AreEqual("%B'%", parameters.Get<string>("Keyword1"));
        Assert.AreEqual(2, parameters.ParameterNames.Count());
        Assert.IsFalse(sql.Contains("B'", StringComparison.Ordinal));
        StringAssert.Contains(sql, "OR (s.SourceNo LIKE @Keyword1");
        StringAssert.Contains(sql, "target.AllocatedQty>target.ReleasedQty");
        StringAssert.Contains(sql, "n.IsDeleted=0 AND n.LifecycleStatus=@MaterialActive");
        Assert.IsFalse(sql.Contains("target.LinkStatus", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("target.TargetType", StringComparison.Ordinal));
        Assert.IsTrue(sql.IndexOf("AND EXISTS", StringComparison.Ordinal) < sql.IndexOf("SELECT COUNT(1) FROM #PackingListSources", StringComparison.Ordinal));
        StringAssert.Contains(sql, "g.IsNonStandard=1 AND g.ProductId IS NULL");
        StringAssert.Contains(sql, "g.DemandQty-COALESCE(d.DeliveredQty,0)");
        StringAssert.Contains(sql, "s.RemainingQty>0");
    }
}
