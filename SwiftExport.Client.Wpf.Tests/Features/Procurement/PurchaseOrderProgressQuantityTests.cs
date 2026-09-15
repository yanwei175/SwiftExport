using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

/// <summary>验证货好状态和未知数量展示，避免把仅日期反馈解释为数量零。</summary>
[TestClass]
public sealed class PurchaseOrderProgressQuantityTests
{
    /// <summary>切行后登记输入为空，空白与非法数字区分，只有明确下降才提示纠错。</summary>
    [TestMethod]
    public void Input_is_independent_from_latest_snapshot()
    {
        var vm = new PurchaseOrderProgressWorkspaceViewModel(null!, null!, null!, null!);
        var dto = new PurchaseOrderProgressLineDto { Quantity = 10m };
        dto.ProgressHistory.Add(new() { Sequence = 1, ReadyQty = 8m, ExpectedReadyDate = new DateTime(2026, 10, 1) });
        vm.SelectedLine = PurchaseOrderProgressLineModel.FromDto(dto);
        Assert.IsNull(vm.ReportReadyQty);
        Assert.IsNull(vm.ReportExpectedReadyDate);
        Assert.IsFalse(vm.IsProgressCorrection);
        vm.ReportReadyQtyInput = "0";
        Assert.AreEqual(0m, vm.ReportReadyQty);
        Assert.IsTrue(vm.IsProgressCorrection);
        vm.ReportReadyQtyInput = "abc";
        Assert.IsTrue(vm.HasInvalidReadyQty);
        vm.ReportReadyQtyInput = " ";
        Assert.IsFalse(vm.HasInvalidReadyQty);
        Assert.IsFalse(vm.IsProgressCorrection);
        vm.ReportNote = "本次备注";
        vm.SelectedLine = null;
        Assert.AreEqual(string.Empty, vm.ReportNote);
        Assert.IsNull(vm.ReportReadyQty);
    }

    /// <summary>无反馈、未知、零、部分及全部备好分别显示。</summary>
    [TestMethod]
    public void Quantity_states_are_distinct()
    {
        var dto = new PurchaseOrderProgressLineDto { Quantity = 10m };
        var model = PurchaseOrderProgressLineModel.FromDto(dto);
        Assert.AreEqual("尚未跟进", model.ReadyStatusText);
        var report = new SupplierProgressReportDto { Sequence = 1 };
        dto.ProgressHistory.Add(report);
        Assert.AreEqual("数量尚未反馈", model.ReadyStatusText);
        Assert.AreEqual("尚未反馈", model.LatestReadyQtyText);
        report.ReadyQty = 0m;
        Assert.AreEqual("尚未备好", model.ReadyStatusText);
        report.ReadyQty = 3.5m;
        Assert.AreEqual("部分备好", model.ReadyStatusText);
        report.ReadyQty = 10m;
        Assert.AreEqual("全部备好", model.ReadyStatusText);
        dto.Quantity = 0m;
        report.ReadyQty = 0m;
        Assert.AreEqual("采购数量为零", model.ReadyStatusText);
    }

    /// <summary>历史乱序时仍取最高序号，收货计算不受货好数量影响。</summary>
    [TestMethod]
    public void Highest_sequence_drives_summary_without_affecting_receipts()
    {
        var dto = new PurchaseOrderProgressLineDto { Quantity = 10m, ReceivedQty = 2m };
        dto.ProgressHistory.Add(new() { Sequence = 1, ReadyQty = 9m });
        dto.ProgressHistory.Add(new() { Sequence = 3, ReadyQty = 4m });
        dto.ProgressHistory.Add(new() { Sequence = 2, ReadyQty = 7m });
        var model = PurchaseOrderProgressLineModel.FromDto(dto);
        Assert.AreEqual(4m, model.LatestReadyQty);
        Assert.AreEqual("8 / 10", model.UnreceivedQtyText);
        Assert.AreEqual(3, model.ProgressHistory[0].Sequence);
    }
}
