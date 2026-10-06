using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Draft;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Query;
using System.Reflection;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>验证采购价格倍率与手工整单优惠快照保持可空、显式持久化且彼此不作自动换算。</summary>
[TestClass]
public sealed class PurchaseOrderDiscountSnapshotContractTests
{
    /// <summary>实体和详情契约必须以可空小数表达未填写或历史未知状态。</summary>
    [TestMethod]
    public void EntityAndDetailDto_ShouldExposeNullableDiscountSnapshots()
    {
        Assert.AreEqual(typeof(decimal?), typeof(PurchaseOrderHead).GetProperty(nameof(PurchaseOrderHead.DiscountRate))!.PropertyType);
        Assert.AreEqual(typeof(decimal?), typeof(PurchaseOrderHead).GetProperty(nameof(PurchaseOrderHead.DiscountAmount))!.PropertyType);
        Assert.AreEqual(typeof(decimal?), typeof(PurchaseOrderDetailDto).GetProperty(nameof(PurchaseOrderDetailDto.DiscountRate))!.PropertyType);
        Assert.AreEqual(typeof(decimal?), typeof(PurchaseOrderDetailDto).GetProperty(nameof(PurchaseOrderDetailDto.DiscountAmount))!.PropertyType);
    }

    /// <summary>独立采购与需求草稿的 PG 写列及详情必须显式覆盖优惠字段，保留可空快照。</summary>
    [TestMethod]
    public void RepositorySql_ShouldPersistAndProjectDiscountSnapshots()
    {
        var commandType = typeof(PostgreSqlProcurementOrderDocumentRepository);
        var fields = (string[])commandType.GetField("HeadInsertFields", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var head = PostgreSqlEntityMetadata.GetMetadata(typeof(PurchaseOrderHead));
        var insert = (string)commandType.GetMethod("InsertSql", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
            [head, fields, new[] { "DemandSourceType", "DemandSourceUid", "DemandSourceNo" }])!;
        var draftFields = (string[])typeof(PostgreSqlPurchaseOrderDraftRepository).GetField("InsertHeadFields", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var draftSource = Read("SwiftExport.Infrastructure", "Repositories", "Procurement", "PurchaseOrders", "Draft", "PostgreSqlPurchaseOrderDraftRepository.cs");
        StringAssert.Contains(draftSource, "InsertHeadFields.Select(x => WriteColumn(Head, x))");
        StringAssert.Contains(draftSource, "InsertHeadFields.Select(x => \"@\" + x)");
        var querySource = Read("SwiftExport.Infrastructure", "Repositories", "Procurement", "PurchaseOrders", "Query", "PostgreSqlProcurementOrderQueryRepository.cs");
        foreach (var field in new[] { ("Currency", "currency"), ("DiscountRate", "discount_rate"), ("DiscountAmount", "discount_amount") })
        {
            StringAssert.Contains(insert, "\"" + field.Item2 + "\"");
            StringAssert.Contains(insert, "@" + field.Item1);
            CollectionAssert.Contains(draftFields, field.Item1);
            Assert.AreEqual("\"" + field.Item2 + "\"", typeof(PostgreSqlPurchaseOrderDraftRepository).GetMethod("WriteColumn", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [head, field.Item1]));
            StringAssert.Contains(querySource, "{H(nameof(PurchaseOrderHead." + field.Item1 + "))} AS \"" + field.Item1 + "\"");
            Assert.AreEqual("h.\"" + field.Item2 + "\"", typeof(PostgreSqlProcurementOrderQueryRepository).GetMethod("H", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [field.Item1]));
        }
    }

    /// <summary>实体生成基线必须声明手工优惠边界且不包含历史回填。</summary>
    [TestMethod]
    public void GeneratedSchema_ShouldKeepUnknownHistoryNullAndAvoidDerivedTotals()
    {
        var sql = new PostgreSqlSchemaGenerator().Generate(typeof(PurchaseOrderHead).Assembly);
        var tableStart = sql.IndexOf("CREATE TABLE \"public\".\"purchase_order_head\"", StringComparison.Ordinal);
        var tableEnd = sql.IndexOf("CREATE TABLE \"public\".\"purchase_order_line\"", tableStart, StringComparison.Ordinal);
        Assert.IsTrue(tableStart >= 0 && tableEnd > tableStart, "无法定位当前 PG 采购头表结构。");
        var purchaseHead = sql[tableStart..tableEnd];

        StringAssert.Contains(purchaseHead, "\"discount_rate\" numeric(9,6) NULL");
        StringAssert.Contains(purchaseHead, "\"discount_amount\" numeric(18,4) NULL");
        StringAssert.Contains(purchaseHead, "ck_purchase_order_head_discount");
        StringAssert.Contains(purchaseHead, "\"discount_rate\" >= 0");
        // 禁止历史回填语句；审计列 updated_date 不是写入操作。
        Assert.IsFalse(purchaseHead.Contains("UPDATE \"public\".\"purchase_order_head\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(purchaseHead.Contains("UPDATE public.purchase_order_head", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(purchaseHead.Contains("discounted_amount", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(purchaseHead.Contains("total_amount", StringComparison.OrdinalIgnoreCase));
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SwiftExport-WebAPI.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("无法定位 SwiftExport 仓库根目录。");
    }
}
