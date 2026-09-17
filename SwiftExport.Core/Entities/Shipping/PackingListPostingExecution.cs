using System.ComponentModel.DataAnnotations.Schema;
using SwiftExport.Core.Common;
using SwiftExport.Core.DatabaseSchema;

namespace SwiftExport.Core.Entities.Shipping;

/// <summary>箱单一次正式过账或反过账的不可变执行证据，不承担采购分摊或库存余额。</summary>
[Table("PackingListPostingExecution")]
[DbAppendOnly]
[DbIndex("UX_PackingListPostingExecution_Command", nameof(CommandId), IsUnique = true)]
[DbIndex("IX_PackingListPostingExecution_Head", nameof(PackingListHeadId), nameof(Operation))]
[DbCheck("CK_PackingListPostingExecution_Operation", "[Operation] IN (1,2)")]
[LogicalReference(nameof(PackingListHeadId), typeof(PackingListHead), IsRequired = true)]
public sealed class PackingListPostingExecution : BaseEntity
{
    /// <summary>实际执行的箱单头。</summary>
    public Guid PackingListHeadId { get; set; }
    /// <summary>外层幂等命令身份，跨操作唯一。</summary>
    public Guid CommandId { get; set; }
    /// <summary>1 正向过账，2 整单反向；不是单据生命周期。</summary>
    public int Operation { get; set; }
    /// <summary>反向时指向原正向执行，正向为空。</summary>
    public Guid? OriginalExecutionId { get; set; }
    /// <summary>库存业务日期，与 InvoiceDate 独立。</summary>
    public DateTime BusinessDate { get; set; }
    /// <summary>操作时箱单编号快照。</summary>
    public string PackingListNo { get; set; } = "";
    /// <summary>执行采用的箱单技术版本。</summary>
    public int ExpectedVersion { get; set; }
    /// <summary>用户及操作完整输入规范化后的 SHA-256。</summary>
    [DbColumn(StoreType = "nvarchar(64)")]
    public string RequestFingerprint { get; set; } = "";
    /// <summary>确认时校验的只读预览令牌。</summary>
    [DbColumn(StoreType = "nvarchar(64)")]
    public string PreviewToken { get; set; } = "";
    /// <summary>计算规则版本，解释历史快照而不重新计算。</summary>
    public int RuleVersion { get; set; } = 1;
    /// <summary>反向原因；正向可以为空。</summary>
    public string? Reason { get; set; }
    /// <summary>完整命令结果 JSON，重放使用原结果而不是重新取号。</summary>
    [DbColumn(StoreType = "nvarchar(max)")]
    public string ResultJson { get; set; } = "";
}
