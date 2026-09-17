using Mapster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExportClient.Features.Shipping.PackingLists.Models;

namespace SwiftExport.Client.Wpf.Tests.Features.Shipping;

/// <summary>验证装箱单集中映射的完整快照与专用写入边界。</summary>
[TestClass]
public sealed class PackingListMappingTests
{
    /// <summary>有效行快照加载、映射及源身份完整保留。</summary>
    [TestMethod]
    public void LineSnapshotRoundTripAndInputMapping()
    {
        var dto = new PackingListLineDto
        {
            Uid = Guid.NewGuid(), Version = 4, PackingListHeadId = Guid.NewGuid(),
            GrossDemandId = Guid.NewGuid(), ProductId = Guid.NewGuid(), LineNumber = 2,
            SkuSnapshot = "SKU", LinkSkuSnapshot = "LINK", CustomerSkuSnapshot = "CUSTOM",
            DescriptionEnSnapshot = "Description", ColorNameEnSnapshot = "Blue", UnitSnapshot = "PCS",
            Qty = 20, PcsPerPackage = 10, PackageCount = 2, NwPerPackage = 3,
            GwPerPackage = 4, CbmPerPackage = .12345m, SubTotalNw = 6, SubTotalGw = 8, SubTotalCbm = .2469m,
            CustomerPoNo = "PO", InvoiceNo = "INV", Marks = "Mark", Note = "Note",
            CreatedDate = new DateTime(2026, 1, 1), CreatedBy = "creator",
            UpdatedDate = new DateTime(2026, 2, 1), UpdatedBy = "editor", ImagePath = "image.png"
        };
        var model = new PackingListLineModel();
        model.Load(dto);
        Assert.IsFalse(model.IsDirty);
        var copy = model.ToDto();
        foreach (var property in typeof(PackingListLineDto).GetProperties())
            Assert.AreEqual(property.GetValue(dto), property.GetValue(copy), property.Name);
        var input = model.Adapt<PackingListDraftLineInput>();
        Assert.AreEqual(dto.Uid, input.Uid);
        Assert.AreEqual(4, input.ExpectedVersion);
        Assert.AreEqual(dto.GrossDemandId, input.GrossDemandId);
        Assert.IsNull(typeof(PackingListDraftLineInput).GetProperty("WarehouseSiteId"));
        model.MarkAsAdded();
        Assert.AreEqual(0, model.Adapt<PackingListDraftLineInput>().ExpectedVersion);
        model.IsChecked = true;
        Assert.IsFalse(model.ToDto().IsDeleted);
    }

    /// <summary>头快照保持状态与审计用于读取，写请求仅包含文档字段。</summary>
    [TestMethod]
    public void HeaderSnapshotAndWriteProjectionRemainSeparate()
    {
        Assert.IsNull(typeof(PackingListHeaderDto).GetProperty("ShippingMark"));
        Assert.IsNull(typeof(PackingListDraftHeaderInput).GetProperty("ShippingMark"));
        Assert.IsNull(typeof(PackingListHeaderModel).GetProperty("ShippingMark"));
        var dto = new PackingListHeaderDto
        {
            Uid = Guid.NewGuid(), Version = 3, PackingListNo = "PL-1", CustomerId = Guid.NewGuid(),
            CustomerNameSnapshot = "客户", CustomerAddressSnapshot = "地址", AttentionSnapshot = "Attention",
            InvoiceDate = new DateTime(2026, 9, 15), ShippingLocationSnapshot = "工厂装柜点", ContainerNo = "C", SealNo = "S", Note = "N",
            Etd = new DateTime(2026, 9, 16), Eta = new DateTime(2026, 9, 18),
            LifecycleStatus = PackingListLifecycleStatusEnum.Active, PostingStatus = PackingListPostingStatusEnum.Posted,
            PostingDate = new DateTime(2026, 9, 14), PostedDate = new DateTime(2026, 9, 14), PostedBy = "poster",
            CreateCommandId = Guid.NewGuid(), PostCommandId = Guid.NewGuid(),
            CreatedBy = "creator", InvoiceNosSummary = "INV"
        };
        var model = new PackingListHeaderModel();
        model.Load(dto);
        model.Lines.LoadFromDtos(new[] { new PackingListLineDto { Uid = Guid.NewGuid(), InvoiceNo = "INV" } });
        Assert.IsFalse(model.IsDirty);
        var copy = model.ToDto();
        foreach (var property in typeof(PackingListHeaderDto).GetProperties())
            Assert.AreEqual(property.GetValue(dto), property.GetValue(copy), property.Name);
        var input = model.Adapt<PackingListDraftHeaderInput>();
        Assert.AreEqual(dto.CustomerId, input.CustomerId);
        Assert.AreEqual(dto.InvoiceDate, input.InvoiceDate);
        Assert.AreEqual(dto.AttentionSnapshot, input.AttentionSnapshot);
        Assert.AreEqual(dto.ShippingLocationSnapshot, input.ShippingLocationSnapshot);
        model.CustomerAddressSnapshot = "新地址";
        Assert.IsTrue(model.IsDirty);
        model.RejectChanges();
        Assert.AreEqual("地址", model.CustomerAddressSnapshot);
        Assert.AreEqual("INV", model.InvoiceNosSummary);
    }
}
