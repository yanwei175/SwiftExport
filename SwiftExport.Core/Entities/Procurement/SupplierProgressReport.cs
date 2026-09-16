using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Procurement;

/// <summary>供应商对采购行的追加式进度反馈。</summary>
[Table("SupplierProgressReport")]
public sealed partial class SupplierProgressReport : BaseEntity
{
    /// <summary>采购行权威 Uid。</summary>
    public Guid PurchaseOrderLineId { get; set; }

    /// <summary>同一采购行内严格递增的反馈序号。</summary>
    public int Sequence { get; set; }
    /// <summary>供应商排产状态，用于判断当前记录允许执行的业务操作。</summary>
    public SupplierArrangementStatusEnum ArrangementStatus { get; set; }
    /// <summary>供应商截至本次报告累计已备妥的数量；空值表示尚未反馈，不代表已入库或当前库存。</summary>
    public decimal? ReadyQty { get; set; }
    /// <summary>该实体保存的预计备妥日期。</summary>
    public DateTime? ExpectedReadyDate { get; set; }
    /// <summary>该实体保存的供应商报告日期。</summary>
    public DateTime SupplierReportedDate { get; set; }

    /// <summary>供应商反馈或跟单备注。</summary>
    public string? Note { get; set; }

    /// <summary>进度修正标记，用于控制该记录的业务适用范围。</summary>
    public bool IsCorrection { get; set; }
    /// <summary>该实体保存的进度修正原因。</summary>
    public string? CorrectionReason { get; set; }
    /// <summary>提交供应商进度报告的命令唯一标识，用于防止重复追加。</summary>
    public Guid CommandId { get; set; }
}
