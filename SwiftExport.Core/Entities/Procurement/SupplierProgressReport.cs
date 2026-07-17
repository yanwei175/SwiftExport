using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Procurement;

/// <summary>供应商对采购行的追加式进度反馈；物理表复用 dbo.FollowUpLog。</summary>
[Table("FollowUpLog")]
public sealed class SupplierProgressReport : BaseEntity
{
    /// <summary>固定为 PurchaseOrderLine，用于区分同表可能承载的其他跟单对象。</summary>
    public string RefLineType { get; set; } = "PurchaseOrderLine";

    /// <summary>采购行权威 Uid；物理列沿用 RefLineId。</summary>
    [Column("RefLineId")]
    public Guid PurchaseOrderLineId { get; set; }

    /// <summary>同一采购行内严格递增的反馈序号。</summary>
    public int Sequence { get; set; }
    public SupplierArrangementStatusEnum ArrangementStatus { get; set; }
    public decimal ReadyQty { get; set; }
    public DateTime? ExpectedReadyDate { get; set; }
    public DateTime SupplierReportedDate { get; set; }

    /// <summary>供应商反馈或跟单备注；物理列沿用 NoteContent。</summary>
    [Column("NoteContent")]
    public string? Note { get; set; }

    public bool IsCorrection { get; set; }
    public string? CorrectionReason { get; set; }
    public Guid CommandId { get; set; }
}
