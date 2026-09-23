using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExportClient.Features.Shipping.PackingLists.Models;

namespace SwiftExport.Client.Wpf.Tests.Features.Shipping;

/// <summary>拼箱使用真实来源行，验证数量、包装去重和编辑恢复。</summary>
[TestClass]
public sealed class PackingListMixedPackingTests
{
    private static PackingListLineDto Row(int n, decimal qty, decimal pcs, Guid? group = null) => new()
    {
        Uid = Guid.NewGuid(), Version = 1, PackingListHeadId = Head, GrossDemandId = Guid.NewGuid(),
        IsNonStandardSnapshot = true, LineNumber = n, Qty = qty, PcsPerPackage = pcs, PackingGroupId = group,
        NwPerPackage = 2, GwPerPackage = 3, CbmPerPackage = .1m, UnitSnapshot = "PCS"
    };
    private static readonly Guid Head = Guid.NewGuid();

    /// <summary>A101、B11四条真实行只计十二箱，加载与展开不产生脏状态。</summary>
    [TestMethod]
    public void TwelveCartonsKeepAllPhysicalQuantities()
    {
        var group = Guid.NewGuid(); var rows = new PackingListLineListModel();
        rows.LoadFromDtos([Row(1, 100, 10), Row(2, 10, 10), Row(3, 1, 1, group), Row(4, 1, 1, group)]);
        Assert.AreEqual(4, rows.Items.Count); Assert.AreEqual(12m, rows.CartonSummary);
        Assert.AreEqual(112m, rows.QtyByUnit["PCS"]); Assert.AreEqual(24m, rows.NetWeightSummary);
        Assert.AreEqual(1, rows.PackingGroups.Count); Assert.IsFalse(rows.HasChanges);
        rows.PackingGroups[0].IsExpanded = false;
        Assert.IsFalse(rows.HasChanges); Assert.AreEqual(4, rows.Items.Count);
    }

    /// <summary>组级编辑同步真实成员，修改数量清空共享指标，撤销恢复原值。</summary>
    [TestMethod]
    public void SharedMetricsSynchronizeAndQuantityInvalidates()
    {
        var id = Guid.NewGuid(); var rows = new PackingListLineListModel();
        rows.LoadFromDtos([Row(1, 1, 1, id), Row(2, 2, 2, id)]);
        rows.PackingGroups[0].NetWeight = 5;
        Assert.IsTrue(rows.Items.All(x => x.NwPerPackage == 5 && x.IsDirty));
        rows.Items[0].Qty = 3;
        Assert.AreEqual(3m, rows.Items[0].PcsPerPackage);
        Assert.IsTrue(rows.Items.All(x => x.NwPerPackage == null && x.GwPerPackage == null && x.CbmPerPackage == null));
        StringAssert.Contains(rows.PackingSummary, "未填写");
        rows.RejectChanges();
        Assert.AreEqual(1m, rows.Items[0].Qty);
        Assert.IsTrue(rows.Items.All(x => x.NwPerPackage == 2));
        Assert.IsFalse(rows.HasChanges);
    }

    /// <summary>删除与恢复不丢组归属；临时空组不伪造产品行。</summary>
    [TestMethod]
    public void EmptyGroupAndDeleteRestoreKeepRealRows()
    {
        var id = Guid.NewGuid(); var rows = new PackingListLineListModel();
        rows.LoadFromDtos([Row(1, 1, 1, id), Row(2, 1, 1, id)]);
        rows.PackingGroups.Add(new(Guid.NewGuid(), true));
        Assert.IsTrue(rows.HasEmptyGroups); Assert.AreEqual(2, rows.Items.Count);
        rows.Remove(rows.Items[0]); Assert.AreEqual(1m, rows.CartonSummary);
        rows.RejectChanges(); Assert.AreEqual(2, rows.Items.Count); Assert.AreEqual(1, rows.PackingGroups.Count);
        Assert.IsFalse(rows.HasEmptyGroups); Assert.IsFalse(rows.Items[0].CanSplitTail);
    }

    /// <summary>映射包含组身份，拼箱不混合单位或发票来源。</summary>
    [TestMethod]
    public void GroupRoundTripPreservesIndependentTextAndUnits()
    {
        var id = Guid.NewGuid(); var a = Row(1, 1, 1, id); var b = Row(2, 1, 1, id);
        a.InvoiceNo = "INV"; b.InvoiceNo = "INV"; b.UnitSnapshot = "SET";
        var rows = new PackingListLineListModel(); rows.LoadFromDtos([a, b]);
        Assert.AreEqual(id, rows.Items[0].ToDto().PackingGroupId);
        Assert.AreEqual(2, rows.QtyByUnit.Count); Assert.AreEqual("INV", rows.InvoiceNosSummary);
    }
}
