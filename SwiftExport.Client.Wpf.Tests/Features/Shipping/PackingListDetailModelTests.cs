using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExportClient.Features.Shipping.PackingLists.Models;

namespace SwiftExport.Client.Wpf.Tests.Features.Shipping;

/// <summary>验证装箱单草稿模型的脏状态、包装计算和逐来源汇总。</summary>
[TestClass]
public sealed class PackingListDetailModelTests
{
    /// <summary>加载不脏、包装编辑可撤销，派生小计不是独立编辑事实。</summary>
    [TestMethod]
    public void Line_LoadEditReject_PreservesTrackingAndComputesPreview()
    {
        var line = new PackingListLineModel();
        line.Load(new PackingListLineDto { Uid = Guid.NewGuid(), Qty = 11, PcsPerPackage = 5, PackageCount = 3, NwPerPackage = 1.23445m });
        Assert.IsFalse(line.IsDirty);
        Assert.AreEqual(3m, line.SuggestedPackageCount);
        Assert.AreEqual(3.7034m, line.SubTotalNw);
        line.PackageCount = 4;
        Assert.IsTrue(line.IsDirty);
        Assert.AreEqual(4.9378m, line.ToDto().SubTotalNw);
        line.Qty = 100;
        Assert.AreEqual(4m, line.PackageCount, "建议数量不得覆盖人工包装数");
        line.RejectChanges();
        Assert.IsFalse(line.IsDirty);
        Assert.AreEqual(3m, line.PackageCount);
    }

    /// <summary>未知包装保持空，极端值不崩溃且阻止保存。</summary>
    [TestMethod]
    public void Line_UnknownInvalidAndOverflow_AreNotValidZeroes()
    {
        var line = new PackingListLineModel { Qty = 1 };
        Assert.IsNull(line.SubTotalNw);
        Assert.IsNull(line.SuggestedPackageCount);
        Assert.IsNull(line.ValidateRow());
        line.PackageCount = 1.5m;
        Assert.IsNotNull(line.ValidateRow());
        line.PackageCount = 2;
        line.NwPerPackage = decimal.MaxValue;
        Assert.IsNull(line.SubTotalNw);
        Assert.IsNotNull(line.ValidateRow());
        line.NwPerPackage = 1;
        line.Qty = decimal.MaxValue;
        line.PcsPerPackage = 0.01m;
        Assert.IsNull(line.SuggestedPackageCount);
        Assert.IsNotNull(line.ValidateRow());
    }

    /// <summary>删除、撤销和行号变化均刷新汇总，子行变化不制造头部脏状态。</summary>
    [TestMethod]
    public void Header_InvoiceSummary_TracksActiveRowsWithoutDirtyingHeader()
    {
        var header = new PackingListHeaderModel();
        header.Load(new PackingListHeaderDto { Uid = Guid.NewGuid() });
        header.Lines.LoadFromDtos(new[]
        {
            new PackingListLineDto { Uid = Guid.NewGuid(), LineNumber = 1, Qty = 2, InvoiceNo = " A ", UnitSnapshot = "PCS" },
            new PackingListLineDto { Uid = Guid.NewGuid(), LineNumber = 2, Qty = 3, InvoiceNo = "B", UnitSnapshot = "SET" },
            new PackingListLineDto { Uid = Guid.NewGuid(), LineNumber = 3, Qty = 4, InvoiceNo = "A", UnitSnapshot = "PCS" }
        });
        Assert.AreEqual("A, B", header.InvoiceNosSummary);
        Assert.IsFalse(header.IsDirty);
        Assert.AreEqual(6m, header.Lines.QtyByUnit["PCS"]);
        Assert.AreEqual(3m, header.Lines.QtyByUnit["SET"]);
        var second = header.Lines.Items[1];
        second.LineNumber = 0;
        Assert.AreEqual("B, A", header.InvoiceNosSummary);
        header.Lines.Remove(second);
        Assert.AreEqual("A", header.InvoiceNosSummary);
        header.Lines.RejectChanges();
        Assert.AreEqual("A, B", header.InvoiceNosSummary);
        Assert.IsFalse(header.Lines.HasChanges);
        Assert.IsFalse(header.IsDirty);
    }

    /// <summary>本地小计只作重算预览，不把不一致的后端快照当作新计算结果。</summary>
    [TestMethod]
    public void Line_LocalPreviewRecomputesSnapshot_AndUnknownMakesTotalUnknown()
    {
        var lines = new PackingListLineListModel();
        lines.LoadFromDtos(new[] { new PackingListLineDto { Uid = Guid.NewGuid(), Qty = 1, PackageCount = 2, NwPerPackage = 3, SubTotalNw = 99 } });
        Assert.AreEqual(6m, lines.Items[0].SubTotalNw);
        Assert.AreEqual(6m, lines.NetWeightSummary);
        lines.AddNew(new PackingListLineDto { Qty = 1 });
        Assert.IsNull(lines.NetWeightSummary);
    }
}
