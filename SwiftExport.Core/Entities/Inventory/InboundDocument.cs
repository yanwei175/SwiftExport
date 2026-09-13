using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Inventory;

/// <summary>入库单头，保存入库来源、目标仓库和过账状态。</summary>
[Table("InboundDocument")]
public sealed class InboundDocument : BaseEntity
{
    /// <summary>该实体保存的业务单据号。</summary>
    public string DocumentNo { get; set; } = string.Empty;
    /// <summary>入库业务类型，同时确定所有明细来源行所属的上游单据类型；一张单不混合采购与生产来源。</summary>
    public InboundDocumentTypeEnum DocumentType { get; set; }
    /// <summary>建立方式：人工选择或自动触发，不改变上游来源类型与可收数量口径。</summary>
    public InboundCreationModeEnum CreationMode { get; set; }
    /// <summary>单据生命周期状态，用于判断当前记录允许执行的业务操作。</summary>
    public InventoryDocumentLifecycleStatusEnum LifecycleStatus { get; set; }
    /// <summary>库存过账状态，用于判断当前记录允许执行的业务操作。</summary>
    public InboundPostingStatusEnum PostingStatus { get; set; }
    /// <summary>关联的仓库站点唯一标识。</summary>
    public Guid WarehouseSiteId { get; set; }
    /// <summary>单据或业务事实形成时冻结的仓库编码历史快照，不随主数据更新。</summary>
    public string? WarehouseCodeSnapshot { get; set; }
    /// <summary>单据或业务事实形成时冻结的仓库名称历史快照，不随主数据更新。</summary>
    public string? WarehouseNameSnapshot { get; set; }
    /// <summary>该实体保存的实际收货日期。</summary>
    public DateTime ReceiptDate { get; set; }
    /// <summary>该实体保存的业务备注。</summary>
    public string? Remark { get; set; }
    /// <summary>该实体保存的过账时间。</summary>
    public DateTime? PostedDate { get; set; }
    /// <summary>该实体保存的过账操作人。</summary>
    public string? PostedBy { get; set; }
    /// <summary>该实体保存的反向时间。</summary>
    public DateTime? ReversedDate { get; set; }
    /// <summary>该实体保存的反向操作人。</summary>
    public string? ReversedBy { get; set; }
    /// <summary>该实体保存的反向原因。</summary>
    public string? ReverseReason { get; set; }
    /// <summary>触发自动入库的装箱单标识，不是供货来源；人工建立时为空。</summary>
    public Guid? TriggerPackingListId { get; set; }
    /// <summary>业务命令唯一标识，用于保障对应业务命令幂等执行。</summary>
    public Guid? CommandId { get; set; }
}
