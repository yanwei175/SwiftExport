using SwiftExport.Contracts.SysEnums.Shipping;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Shipping;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Application.Tests.Shipping;

/// <summary>冻结新装箱单实体结构，不连接数据库或假定目标表已迁移。</summary>
[TestClass]
public sealed class PackingListEntityContractTests
{
    /// <summary>目标实体仍保留公共身份、审计及并发能力。</summary>
    [TestMethod]
    public void EntitiesKeepIdentityAndTargetMappings()
    {
        Assert.AreEqual("PackingListHead", typeof(PackingListHead).GetCustomAttributes(typeof(TableAttribute), false).Cast<TableAttribute>().Single().Name);
        Assert.AreEqual("PackingListLine", typeof(PackingListLine).GetCustomAttributes(typeof(TableAttribute), false).Cast<TableAttribute>().Single().Name);
        Assert.IsTrue(typeof(BaseEntity).IsAssignableFrom(typeof(PackingListHead)));
        Assert.IsTrue(typeof(BaseEntity).IsAssignableFrom(typeof(PackingListLine)));
        var id = Guid.NewGuid();
        Assert.AreEqual(id, new PackingListHead { Uid = id }.Uid);
        Assert.AreEqual(1, new PackingListLine().Version);
    }

    /// <summary>所有已确认字段都有固定类型，未知包装和草稿仓库不强填零。</summary>
    [TestMethod]
    public void FieldsMatchApprovedContract()
    {
        Assert.AreEqual(typeof(string), typeof(PackingListHead).GetProperty("PackingListNo")!.PropertyType);
        Assert.AreEqual(typeof(Guid), typeof(PackingListHead).GetProperty("CustomerId")!.PropertyType);
        Assert.AreEqual(typeof(string), typeof(PackingListHead).GetProperty("CustomerNameSnapshot")!.PropertyType);
        Assert.AreEqual(typeof(string), typeof(PackingListHead).GetProperty("CustomerAddressSnapshot")!.PropertyType);
        Assert.AreEqual(typeof(string), typeof(PackingListHead).GetProperty("AttentionSnapshot")!.PropertyType);
        Assert.AreEqual(typeof(DateTime), typeof(PackingListHead).GetProperty("InvoiceDate")!.PropertyType);
        Assert.AreEqual(typeof(string), typeof(PackingListHead).GetProperty("ContainerNo")!.PropertyType);
        Assert.AreEqual(typeof(string), typeof(PackingListHead).GetProperty("SealNo")!.PropertyType);
        Assert.IsNull(typeof(PackingListHead).GetProperty("ShippingMark"));
        Assert.AreEqual(typeof(string), typeof(PackingListLine).GetProperty("Marks")!.PropertyType);
        Assert.AreEqual(typeof(string), typeof(PackingListHead).GetProperty("Note")!.PropertyType);
        Assert.AreEqual(typeof(DateTime?), typeof(PackingListHead).GetProperty("Etd")!.PropertyType);
        Assert.AreEqual(typeof(DateTime?), typeof(PackingListHead).GetProperty("Eta")!.PropertyType);
        Assert.AreEqual(typeof(PackingListLifecycleStatusEnum), typeof(PackingListHead).GetProperty("LifecycleStatus")!.PropertyType);
        Assert.AreEqual(typeof(PackingListPostingStatusEnum), typeof(PackingListHead).GetProperty("PostingStatus")!.PropertyType);
        Assert.AreEqual(typeof(DateTime?), typeof(PackingListHead).GetProperty("PostingDate")!.PropertyType);
        Assert.AreEqual(typeof(DateTime?), typeof(PackingListHead).GetProperty("PostedDate")!.PropertyType);
        Assert.AreEqual(typeof(string), typeof(PackingListHead).GetProperty("PostedBy")!.PropertyType);
        Assert.AreEqual(typeof(DateTime?), typeof(PackingListHead).GetProperty("ReversedDate")!.PropertyType);
        Assert.AreEqual(typeof(string), typeof(PackingListHead).GetProperty("ReversedBy")!.PropertyType);
        Assert.AreEqual(typeof(string), typeof(PackingListHead).GetProperty("ReverseReason")!.PropertyType);
        Assert.AreEqual(typeof(Guid), typeof(PackingListHead).GetProperty("CreateCommandId")!.PropertyType);
        Assert.AreEqual(typeof(Guid?), typeof(PackingListHead).GetProperty("PostCommandId")!.PropertyType);
        Assert.AreEqual(typeof(Guid?), typeof(PackingListHead).GetProperty("ReverseCommandId")!.PropertyType);
        Assert.AreEqual(typeof(Guid), typeof(PackingListLine).GetProperty("PackingListHeadId")!.PropertyType);
        Assert.AreEqual(typeof(int), typeof(PackingListLine).GetProperty("LineNumber")!.PropertyType);
        Assert.AreEqual(typeof(Guid?), typeof(PackingListLine).GetProperty("GrossDemandId")!.PropertyType);
        Assert.AreEqual(typeof(Guid?), typeof(PackingListLine).GetProperty("ProductId")!.PropertyType);
        Assert.AreEqual(typeof(bool), typeof(PackingListLine).GetProperty("IsNonStandardSnapshot")!.PropertyType);
        Assert.AreEqual(typeof(string), typeof(PackingListLine).GetProperty("SourceTypeSnapshot")!.PropertyType);
        Assert.AreEqual(typeof(string), typeof(PackingListLine).GetProperty("SourceDocumentNoSnapshot")!.PropertyType);
        Assert.AreEqual(typeof(int?), typeof(PackingListLine).GetProperty("SourceLineNumberSnapshot")!.PropertyType);
        Assert.AreEqual(typeof(string), typeof(PackingListLine).GetProperty("SkuSnapshot")!.PropertyType);
        Assert.AreEqual(typeof(string), typeof(PackingListLine).GetProperty("LinkSkuSnapshot")!.PropertyType);
        Assert.AreEqual(typeof(string), typeof(PackingListLine).GetProperty("CustomerSkuSnapshot")!.PropertyType);
        Assert.AreEqual(typeof(string), typeof(PackingListLine).GetProperty("DescriptionEnSnapshot")!.PropertyType);
        Assert.AreEqual(typeof(string), typeof(PackingListLine).GetProperty("ColorNameEnSnapshot")!.PropertyType);
        Assert.AreEqual(typeof(string), typeof(PackingListLine).GetProperty("UnitSnapshot")!.PropertyType);
        Assert.AreEqual(typeof(decimal), typeof(PackingListLine).GetProperty("Qty")!.PropertyType);
        Assert.AreEqual(typeof(decimal?), typeof(PackingListLine).GetProperty("PcsPerPackage")!.PropertyType);
        Assert.AreEqual(typeof(decimal?), typeof(PackingListLine).GetProperty("PackageCount")!.PropertyType);
        Assert.AreEqual(typeof(decimal?), typeof(PackingListLine).GetProperty("NwPerPackage")!.PropertyType);
        Assert.AreEqual(typeof(decimal?), typeof(PackingListLine).GetProperty("GwPerPackage")!.PropertyType);
        Assert.AreEqual(typeof(decimal?), typeof(PackingListLine).GetProperty("CbmPerPackage")!.PropertyType);
        Assert.AreEqual(typeof(decimal?), typeof(PackingListLine).GetProperty("SubTotalNw")!.PropertyType);
        Assert.AreEqual(typeof(decimal?), typeof(PackingListLine).GetProperty("SubTotalGw")!.PropertyType);
        Assert.AreEqual(typeof(decimal?), typeof(PackingListLine).GetProperty("SubTotalCbm")!.PropertyType);
        Assert.AreEqual(typeof(string), typeof(PackingListLine).GetProperty("CustomerPoNo")!.PropertyType);
        Assert.AreEqual(typeof(string), typeof(PackingListLine).GetProperty("InvoiceNo")!.PropertyType);
        Assert.AreEqual(typeof(string), typeof(PackingListLine).GetProperty("Marks")!.PropertyType);
        Assert.AreEqual(typeof(string), typeof(PackingListLine).GetProperty("Note")!.PropertyType);
        foreach (var field in new[] { "WarehouseSiteId", "WarehouseCodeSnapshot", "WarehouseNameSnapshot", "ShippingLocationSnapshot" })
            Assert.IsNull(typeof(PackingListLine).GetProperty(field));
        Assert.AreEqual(typeof(string), typeof(PackingListHead).GetProperty("ShippingLocationSnapshot")!.PropertyType);
        Assert.IsNull(new PackingListLine().PackageCount);
        Assert.IsNull(new PackingListLine().SubTotalNw);
    }

    /// <summary>来源允许拆行；旧采购、物料和预览关联不得继续成为实体权威。</summary>
    [TestMethod]
    public void GrossDemandIsOnlyDeliveryAnchor()
    {
        foreach (var field in new[] { "PurchaseOrderLineId", "NetDemandId", "NetDemandAllocationId", "DemandExecutionLinkId", "SalesOrderId", "SalesOrderLineId", "SourceSupplierId", "CommandItemId", "SourceDemandVersionSnapshot", "SourceVersionSnapshot" })
            Assert.IsNull(typeof(PackingListLine).GetProperty(field), field);
        foreach (var field in new[] { "InvoiceNo", "InvoiceNosSummary", "PackingDate", "ShipmentDate", "PostPreviewToken", "ReversePreviewToken", "CreateRequestFingerprint" })
            Assert.IsNull(typeof(PackingListHead).GetProperty(field), field);
        var source = Guid.NewGuid();
        var first = new PackingListLine { GrossDemandId = source, LineNumber = 1 };
        var second = new PackingListLine { GrossDemandId = source, LineNumber = 2 };
        Assert.AreEqual(first.GrossDemandId, second.GrossDemandId);
        Assert.AreNotEqual(first.Uid, second.Uid);
    }

    /// <summary>修改文档日期不会改写库存业务日期。</summary>
    [TestMethod]
    public void InvoiceDateDoesNotChangePostingDate()
    {
        var date = new DateTime(2026, 9, 1);
        var head = new PackingListHead { PostingDate = date, InvoiceDate = date };
        head.InvoiceDate = date.AddDays(10);
        Assert.AreEqual(date, head.PostingDate);
    }
}
