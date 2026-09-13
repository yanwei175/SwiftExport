using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Infrastructure.Repositories.Inventory;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>验证普通入库来源的写查询持有虚拟仓锁，且只读搜索不会扩大为更新锁。</summary>
[TestClass]
public sealed class InboundSourceWarehouseLockTests
{
    /// <summary>验证客供件写查询同时包含来源与活动供应商虚拟仓的锁提示。</summary>
    [TestMethod]
    public void CustomerSuppliedWriteQuery_ShouldIncludeSourceAndWarehouseLockHints()
    {
        var sql = BuildSql(InboundDocumentTypeEnum.CustomerSuppliedReceipt, true);

        StringAssert.Contains(sql, "NetDemandAllocation a WITH(UPDLOCK,HOLDLOCK)");
        StringAssert.Contains(sql, "WarehouseSite w WITH(UPDLOCK,HOLDLOCK)");
        StringAssert.Contains(sql, "w.IsActive=1 AND ISNULL(w.IsDeleted,0)=0");
    }

    /// <summary>验证采购收货写查询同时包含来源与活动供应商虚拟仓的锁提示。</summary>
    [TestMethod]
    public void PurchaseReceiptWriteQuery_ShouldIncludeSourceAndWarehouseLockHints()
    {
        var sql = BuildSql(InboundDocumentTypeEnum.PurchaseReceipt, true);

        StringAssert.Contains(sql, "DemandExecutionLink l WITH(UPDLOCK,HOLDLOCK)");
        StringAssert.Contains(sql, "WarehouseSite w WITH(UPDLOCK,HOLDLOCK)");
        StringAssert.Contains(sql, "w.IsActive=1 AND ISNULL(w.IsDeleted,0)=0");
    }

    /// <summary>验证普通来源搜索的两个分支都不申请 UPDLOCK/HOLDLOCK。</summary>
    [TestMethod]
    public void ReadQueries_ShouldNotUseWriteLocks()
    {
        foreach (var type in new[]
                 {
                     InboundDocumentTypeEnum.CustomerSuppliedReceipt,
                     InboundDocumentTypeEnum.PurchaseReceipt
                 })
        {
            var sql = BuildSql(type, false);
            Assert.IsFalse(sql.Contains("WITH(UPDLOCK,HOLDLOCK)", StringComparison.Ordinal), type.ToString());
        }
    }

    /// <summary>调用仓储实际 SQL 生成路径，避免通过读取源码文本伪造锁验证。</summary>
    private static string BuildSql(InboundDocumentTypeEnum type, bool lockRows)
    {
        var method = typeof(InboundSourceRepository).GetMethod(
            "BuildLoadSql", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("未找到入库来源 SQL 生成方法。");
        return (string)(method.Invoke(null, [type, lockRows])
            ?? throw new AssertFailedException("入库来源 SQL 为空。"));
    }
}
