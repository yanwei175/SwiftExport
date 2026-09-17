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
        Assert.AreEqual(3, parameters.ParameterNames.Count());
        Assert.AreEqual(SwiftExport.Contracts.SysEnums.StaticEnums.DocumentTypeStaticsEnum.ComplaintReplenishment,
            parameters.Get<string>("AfterSalesSourceType"));
        Assert.IsFalse(sql.Contains("B'", StringComparison.Ordinal));
        StringAssert.Contains(sql, "OR (s.SourceNo LIKE @Keyword1");
        StringAssert.Contains(sql, "target.AllocatedQty>target.ReleasedQty");
        StringAssert.Contains(sql, "n.IsDeleted=0 AND n.LifecycleStatus=@MaterialActive");
        Assert.IsFalse(sql.Contains("target.LinkStatus", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("target.TargetType", StringComparison.Ordinal));
        Assert.IsTrue(sql.IndexOf("OR EXISTS", StringComparison.Ordinal) < sql.IndexOf("SELECT COUNT(1) FROM #PackingListSources", StringComparison.Ordinal));
        StringAssert.Contains(sql, "g.IsNonStandard=1 AND g.ProductId IS NULL");
        StringAssert.Contains(sql, "g.DemandQty-COALESCE(d.DeliveredQty,0)");
        StringAssert.Contains(sql, "s.RemainingQty>0");
    }

    /// <summary>售后仅豁免执行关系，共同条件和关键词仍在分页前生效，交付及图片投影保持不变。</summary>
    [TestMethod]
    public void AfterSalesExceptionPreservesCommonEligibilityAndDeliveryProjection()
    {
        var method = typeof(PackingListRepository).GetMethod("BuildSourceSearchSql", BindingFlags.NonPublic | BindingFlags.Static)!;
        var sql = (string)method.Invoke(null, [new PackingListSourceSearchRequest { Keyword = "model" }, new DynamicParameters()])!;
        var normalized = System.Text.RegularExpressions.Regex.Replace(sql, @"\s+", " ");
        StringAssert.Contains(normalized, "WHERE s.IsActive=1 AND s.CustomerId=@CustomerId AND s.RemainingQty>0 AND (s.SourceType=@AfterSalesSourceType OR EXISTS (");
        StringAssert.Contains(normalized, "WHERE n.GrossDemandId=s.GrossDemandId AND n.IsDeleted=0 AND n.LifecycleStatus=@MaterialActive AND target.IsDeleted=0 AND target.AllocatedQty>target.ReleasedQty )) AND (");
        StringAssert.Contains(sql, "l.IsDeleted=0 AND h.IsDeleted=0 AND h.PostingStatus=@Posted");
        StringAssert.Contains(sql, "GROUP BY l.GrossDemandId");
        StringAssert.Contains(sql, "g.DemandQty-COALESCE(d.DeliveredQty,0)");
        StringAssert.Contains(sql, "g.ImagePathSnapshot AS ImagePath");
        StringAssert.Contains(sql, "s.ImagePath");
        Assert.IsTrue(sql.IndexOf("SELECT COUNT(1) FROM #PackingListSources", StringComparison.Ordinal) < sql.IndexOf("OFFSET @Skip", StringComparison.Ordinal));
    }
}
