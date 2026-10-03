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
    /// <summary>精确销售发票号或系统售后号在计数前过滤，用户文本参数化，PO 不参与查找。</summary>
    [TestMethod]
    public void InvoiceFiltersAreExactAndAppliedBeforeCount()
    {
        var parameters = new DynamicParameters(); var product = Guid.NewGuid();
        var method = typeof(PackingListRepository).GetMethod("BuildSourceSearchSql", BindingFlags.NonPublic | BindingFlags.Static)!;
        var sql = (string)method.Invoke(null, [new PackingListSourceSearchRequest
        { ProductId = product, IsNonStandard = false, InvoiceNumbers = [" inv'01 ", "AFS01"] }, parameters])!;
        CollectionAssert.AreEqual(new[] { "INV'01", "AFS01" }, parameters.Get<string[]>("InvoiceNumbers"));
        Assert.AreEqual(product, parameters.Get<Guid>("ProductId")); Assert.IsFalse(parameters.Get<bool>("IsNonStandard"));
        StringAssert.Contains(sql, "THEN soh.InvoiceNo WHEN s.SourceType=N'CR' THEN ash.AfterSalesNo");
        StringAssert.Contains(sql, "IN @InvoiceNumbers"); Assert.IsFalse(sql.Contains("CustomerPoNo", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("INV'01", StringComparison.Ordinal));
        Assert.IsTrue(sql.IndexOf("IN @InvoiceNumbers", StringComparison.Ordinal) < sql.IndexOf("SELECT COUNT(1)", StringComparison.Ordinal));
    }
    /// <summary>批量词按 OR 参数化，草稿来源无需采购执行关系。</summary>
    [TestMethod]
    public void BatchKeywordsAndTargetAreFilteredBeforePaging()
    {
        var parameters = new DynamicParameters();
        var method = typeof(PackingListRepository).GetMethod("BuildSourceSearchSql", BindingFlags.NonPublic | BindingFlags.Static)!;
        var sql = (string)method.Invoke(null, [new PackingListSourceSearchRequest
        { Keywords = [" A% ", "a%", "B'"], Keyword = "A%" }, parameters])!;
        Assert.AreEqual("%A~%%", parameters.Get<string>("Keyword0"));
        Assert.AreEqual("%B'%", parameters.Get<string>("Keyword1"));
        Assert.AreEqual(5, parameters.ParameterNames.Count());
        Assert.IsFalse(sql.Contains("B'", StringComparison.Ordinal));
        StringAssert.Contains(sql, "OR (s.SourceNo LIKE @Keyword1");
        Assert.IsFalse(sql.Contains("dbo.NetDemand", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("DemandExecutionLink", StringComparison.Ordinal));
        StringAssert.Contains(sql, "(@ProductId IS NULL OR s.ProductId=@ProductId)");
        StringAssert.Contains(sql, "(@IsNonStandard IS NULL OR s.IsNonStandard=@IsNonStandard)");
        StringAssert.Contains(sql, "g.IsNonStandard=1 AND g.ProductId IS NULL");
        StringAssert.Contains(sql, "g.DemandQty-COALESCE(d.DeliveredQty,0)");
        StringAssert.Contains(sql, "s.RemainingQty>0");
    }

    /// <summary>普通与售后采用相同草稿准入，共同条件在分页前生效。</summary>
    [TestMethod]
    public void AfterSalesExceptionPreservesCommonEligibilityAndDeliveryProjection()
    {
        var method = typeof(PackingListRepository).GetMethod("BuildSourceSearchSql", BindingFlags.NonPublic | BindingFlags.Static)!;
        var sql = (string)method.Invoke(null, [new PackingListSourceSearchRequest { Keyword = "model" }, new DynamicParameters()])!;
        var normalized = System.Text.RegularExpressions.Regex.Replace(sql, @"\s+", " ");
        StringAssert.Contains(normalized, "WHERE s.IsActive=1 AND s.CustomerId=@CustomerId AND s.RemainingQty>0");
        Assert.IsFalse(normalized.Contains("OR EXISTS", StringComparison.Ordinal));
        StringAssert.Contains(sql, "l.IsDeleted=0 AND h.IsDeleted=0 AND h.PostingStatus=@Posted");
        StringAssert.Contains(sql, "GROUP BY l.GrossDemandId");
        StringAssert.Contains(sql, "g.DemandQty-COALESCE(d.DeliveredQty,0)");
        StringAssert.Contains(sql, "g.ImagePathSnapshot AS ImagePath");
        StringAssert.Contains(sql, "s.ImagePath");
        Assert.IsTrue(sql.IndexOf("SELECT COUNT(1) FROM #PackingListSources", StringComparison.Ordinal) < sql.IndexOf("OFFSET @Skip", StringComparison.Ordinal));
    }
}
