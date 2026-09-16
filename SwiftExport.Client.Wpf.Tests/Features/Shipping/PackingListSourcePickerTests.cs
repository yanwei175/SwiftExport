using Mapster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExportClient.Features.Shipping.PackingLists.Models;

namespace SwiftExport.Client.Wpf.Tests.Features.Shipping;

/// <summary>验证待发货选源快照、图片和本地选择边界。</summary>
[TestClass]
public sealed class PackingListSourcePickerTests
{
    /// <summary>图片贯穿候选、Picker和接纳明细，不制造库存或发票事实。</summary>
    [TestMethod]
    public void CandidateKeepsImageAndSourceThroughSelection()
    {
        var dto = new PackingListSourceCandidateDto
        {
            GrossDemandId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), ProductId = Guid.NewGuid(),
            DemandVersion = 5, SourceVersion = 2, ImagePath = "products/a.png", RemainingQty = 8,
            SkuSnapshot = "A", UnitSnapshot = "PCS", PcsPerPackage = 4
        };
        var row = dto.Adapt<PackingListSourcePickerRowModel>();
        Assert.IsFalse(row.IsChecked);
        Assert.AreEqual(0m, row.SelectedQty);
        row.IsChecked = true;
        row.SelectedQty = 3;
        Assert.IsNull(row.ValidateSelection());
        var selection = row.Adapt<PackingListSourcePickerSelection>();
        Assert.AreEqual(3m, selection.Qty);
        foreach (var property in typeof(PackingListSourceCandidateDto).GetProperties())
            Assert.AreEqual(property.GetValue(dto), property.GetValue(selection.Source), property.Name);
        var line = selection.Source.Adapt<PackingListLineDto>();
        Assert.AreEqual(dto.ImagePath, line.ImagePath);
        Assert.AreEqual(dto.GrossDemandId, line.GrossDemandId);
        Assert.AreEqual(Guid.Empty, line.Uid);
        Assert.AreEqual(0m, line.Qty);
        Assert.IsNull(line.InvoiceNo);
    }

    /// <summary>未知余额不当作零或全部可交付，输入超量不得确认。</summary>
    [TestMethod]
    public void SelectionRejectsUnknownOrExcessQuantity()
    {
        var dto = new PackingListSourceCandidateDto { GrossDemandId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), ProductId = Guid.NewGuid() };
        var unknown = dto.Adapt<PackingListSourcePickerRowModel>();
        Assert.IsNull(unknown.RemainingQty);
        Assert.IsNull(unknown.ImagePath);
        Assert.IsNotNull(unknown.ValidateSelection());
        dto.RemainingQty = 2;
        var row = dto.Adapt<PackingListSourcePickerRowModel>();
        row.SelectedQty = 3;
        Assert.IsNotNull(row.ValidateSelection());
        row.SelectedQty = 2;
        Assert.IsNull(row.ValidateSelection());
        row.SelectedQty = 0;
        Assert.IsNotNull(row.ValidateSelection());
    }

    /// <summary>搜索保留客户隔离和公共分页语义。</summary>
    [TestMethod]
    public void SearchMapsCustomerKeywordAndPaging()
    {
        var model = new PackingListSourcePickerSearchModel { CustomerId = Guid.NewGuid(), Keyword = "SKU", PageIndex = 2 };
        var request = model.Adapt<PackingListSourceSearchRequest>();
        Assert.AreEqual(model.CustomerId, request.CustomerId);
        Assert.AreEqual("SKU", request.Keyword);
        Assert.AreEqual(2, request.PageIndex);
        Assert.AreEqual(20, request.PageSize);
    }

    /// <summary>非标来源允许缺少产品身份，并把来源身份快照映射到新装箱行。</summary>
    [TestMethod]
    public void NonStandardSourceMapsWithoutProductOrWarehouse()
    {
        var dto = new PackingListSourceCandidateDto
        {
            GrossDemandId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), IsNonStandard = true,
            SourceType = "MD", SourceNo = "MD-001", SourceLineNumber = 3,
            RemainingQty = 2, DemandQty = 2, SkuSnapshot = "CUSTOM"
        };
        var row = dto.Adapt<PackingListSourcePickerRowModel>();
        row.SelectedQty = 1;
        Assert.IsNull(row.ValidateSelection());
        var line = dto.Adapt<PackingListLineDto>();
        Assert.IsTrue(line.IsNonStandardSnapshot);
        Assert.IsNull(line.ProductId);
        Assert.AreEqual("MD", line.SourceTypeSnapshot);
        Assert.AreEqual("MD-001", line.SourceDocumentNoSnapshot);
        Assert.AreEqual(3, line.SourceLineNumberSnapshot);
    }
}
