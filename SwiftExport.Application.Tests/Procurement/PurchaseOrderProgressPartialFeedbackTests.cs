using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.Procurement;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>验证独立字段反馈形成追加快照，无需数据库环境。</summary>
[TestClass]
public sealed class PurchaseOrderProgressPartialFeedbackTests
{
    /// <summary>仅备注继承数量和日期，而未知数量首次变成零不构成纠错。</summary>
    [TestMethod]
    public void Note_only_inherits_snapshot_and_first_zero_is_not_correction()
    {
        var previous = new SupplierProgressReport { Sequence = 2, ReadyQty = 7m, ExpectedReadyDate = new DateTime(2026, 10, 1) };
        var report = PurchaseOrderProgressReportService.CreateReport(Guid.NewGuid(), new() { Note = "  再次催货  " }, previous);
        Assert.AreEqual(7m, report.ReadyQty);
        Assert.AreEqual(new DateTime(2026, 10, 1), report.ExpectedReadyDate);
        Assert.AreEqual("再次催货", report.Note);
        var firstZero = PurchaseOrderProgressReportService.CreateReport(Guid.NewGuid(), new() { ReadyQty = 0m }, new() { Sequence = 1 });
        Assert.AreEqual(0m, firstZero.ReadyQty);
        Assert.IsFalse(firstZero.IsCorrection);
    }

    /// <summary>首次仅日期或备注不生成零数量；序号从一开始。</summary>
    [TestMethod]
    public void First_date_or_note_keeps_quantity_unknown()
    {
        foreach (var command in new[] { new ReportPurchaseOrderProgressCommand { ExpectedReadyDate = new DateTime(2026, 10, 1) }, new ReportPurchaseOrderProgressCommand { Note = "等待回复" } })
        {
            var report = PurchaseOrderProgressReportService.CreateReport(Guid.NewGuid(), command, null);
            Assert.IsNull(report.ReadyQty);
            Assert.AreEqual(1, report.Sequence);
            Assert.AreEqual(SupplierArrangementStatusEnum.Unknown, report.ArrangementStatus);
        }
    }

    /// <summary>只更新日期时继承数量和安排，备注不继承，旧报告保持原样。</summary>
    [TestMethod]
    public void Date_only_inherits_quantity_without_overwriting_history()
    {
        var previous = new SupplierProgressReport { Sequence = 7, ReadyQty = 8m, ExpectedReadyDate = new DateTime(2026, 10, 1), Note = "旧备注", ArrangementStatus = SupplierArrangementStatusEnum.Arranged };
        var report = PurchaseOrderProgressReportService.CreateReport(Guid.NewGuid(), new() { ExpectedReadyDate = new DateTime(2026, 10, 5) }, previous);
        Assert.AreEqual(8, report.Sequence);
        Assert.AreEqual(8m, report.ReadyQty);
        Assert.AreEqual(SupplierArrangementStatusEnum.Arranged, report.ArrangementStatus);
        Assert.IsNull(report.Note);
        Assert.IsFalse(report.IsCorrection);
        Assert.AreEqual(new DateTime(2026, 10, 1), previous.ExpectedReadyDate);
        Assert.AreEqual("旧备注", previous.Note);
    }

    /// <summary>显式零是数量纠错，必须有原因；留空日期保留原交期。</summary>
    [TestMethod]
    public void Explicit_zero_requires_correction_reason_and_keeps_date()
    {
        var previous = new SupplierProgressReport { Sequence = 1, ReadyQty = 5m, ExpectedReadyDate = new DateTime(2026, 10, 1) };
        var command = new ReportPurchaseOrderProgressCommand { ReadyQty = 0m };
        Assert.ThrowsExactly<ProcurementOperationException>(() => PurchaseOrderProgressReportService.CreateReport(Guid.NewGuid(), command, previous));
        command.CorrectionReason = "供应商更正";
        var report = PurchaseOrderProgressReportService.CreateReport(Guid.NewGuid(), command, previous);
        Assert.AreEqual(0m, report.ReadyQty);
        Assert.AreEqual(previous.ExpectedReadyDate, report.ExpectedReadyDate);
        Assert.IsTrue(report.IsCorrection);
        Assert.AreEqual(5m, previous.ReadyQty);
    }

    /// <summary>全空或只有纠错原因不能追加；显式零是有效输入。</summary>
    [TestMethod]
    public void Empty_feedback_is_rejected_but_zero_is_accepted()
    {
        var command = new ReportPurchaseOrderProgressCommand { CommandId = Guid.NewGuid(), PurchaseOrderLineId = Guid.NewGuid(), ExpectedPurchaseOrderVersion = 1, ExpectedLineVersion = 1, Note = "  ", CorrectionReason = "原因" };
        Assert.ThrowsExactly<ProcurementOperationException>(() => PurchaseOrderProgressReportService.ValidateReportCommand(Guid.NewGuid(), command));
        command.ReadyQty = 0m;
        PurchaseOrderProgressReportService.ValidateReportCommand(Guid.NewGuid(), command);
        command.ReadyQty = -1m;
        Assert.ThrowsExactly<ProcurementOperationException>(() => PurchaseOrderProgressReportService.ValidateReportCommand(Guid.NewGuid(), command));
    }
}
