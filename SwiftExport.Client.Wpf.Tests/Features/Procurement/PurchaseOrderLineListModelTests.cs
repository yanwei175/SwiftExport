using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

/// <summary>采购详情统一行集合的状态、回滚与校验回归测试。</summary>
[TestClass]
public sealed class PurchaseOrderLineListModelTests
{
    /// <summary>加载后编辑白名单字段产生 Modified，拒绝修改恢复原基线。</summary>
    [TestMethod]
    public void LoadedLineTracksAndRejectsBusinessChanges()
    {
        var lines = new PurchaseOrderLineListModel();
        lines.LoadFromDtos([ExistingLine()]);

        lines.Items[0].SupplierSku = "NEW-SKU";
        Assert.IsTrue(lines.HasChanges);
        Assert.IsTrue(lines.Items[0].IsModified);

        lines.RejectChanges();
        Assert.IsFalse(lines.HasChanges);
        Assert.AreEqual("OLD-SKU", lines.Items[0].SupplierSku);
    }

    /// <summary>创建行可彻底删除，且缺少产品时整表校验返回稳定行级错误。</summary>
    [TestMethod]
    public async Task AddedLineCanBeRemovedAndRequiresProduct()
    {
        var lines = new PurchaseOrderLineListModel();
        var added = lines.AddNew(new PurchaseOrderLineDetailDto { Uid = Guid.NewGuid(), Quantity = 1m });

        StringAssert.Contains(await lines.ValidateAllAsync(), "必须选择产品");
        lines.Remove(added);

        Assert.HasCount(0, lines.Items);
        Assert.IsFalse(lines.HasChanges);
    }

    private static PurchaseOrderLineDetailDto ExistingLine() => new()
    {
        Uid = Guid.NewGuid(), Version = 1, ProductId = Guid.NewGuid(), LineNumber = 1,
        Sku = "SKU-1", UnitSnapshot = "PCS", Quantity = 10m,
        SupplierSku = "OLD-SKU"
    };
}
