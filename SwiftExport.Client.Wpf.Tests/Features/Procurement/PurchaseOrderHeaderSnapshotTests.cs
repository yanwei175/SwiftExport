using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

/// <summary>验证详情页接收采购商业快照，不换算金额或保留上一单的未知值。</summary>
[TestClass]
public sealed class PurchaseOrderHeaderSnapshotTests
{
    /// <summary>倍率与手工优惠独立展示，查询总额不能被客户端再次打折。</summary>
    [TestMethod]
    public void Load_PreservesDiscountSnapshotsAndTotal()
    {
        var model = new PurchaseOrderHeaderEditorModel();
        model.Load(new PurchaseOrderDetailDto
        {
            DiscountRate = 0.956789m, DiscountAmount = 125.5m, CnyTotalAmount = 1000m
        });

        Assert.AreEqual(0.956789m, model.DiscountRate);
        Assert.AreEqual(125.5m, model.DiscountAmount);
        Assert.AreEqual(1000m, model.TotalAmount);
    }

    /// <summary>后续订单未填写时必须清空旧快照，显式零值则保持为零。</summary>
    [TestMethod]
    public void Load_ReplacesPreviousDiscountsWithNullOrZero()
    {
        var model = new PurchaseOrderHeaderEditorModel();
        model.Load(new PurchaseOrderDetailDto { DiscountRate = 0.9m, DiscountAmount = 20m });
        model.Load(new PurchaseOrderDetailDto());
        Assert.IsNull(model.DiscountRate);
        Assert.IsNull(model.DiscountAmount);

        model.Load(new PurchaseOrderDetailDto { DiscountRate = 0m, DiscountAmount = 0m });
        Assert.AreEqual(0m, model.DiscountRate);
        Assert.AreEqual(0m, model.DiscountAmount);
    }
}
