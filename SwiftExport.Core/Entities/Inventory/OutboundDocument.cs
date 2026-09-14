using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Inventory;

/// <summary>出库单头实体，保存库存出库的来源仓库及过账和反向状态。</summary>
[Table("OutboundDocument")]
public sealed class OutboundDocument : BaseEntity
{
    /// <summary>该实体保存的业务单据号。</summary>
    public string DocumentNo { get; set; } = string.Empty;
    /// <summary>该实体保存的业务单据类型。</summary>
    public OutboundDocumentTypeEnum DocumentType { get; set; }
    /// <summary>单据生命周期状态，用于判断当前记录允许执行的业务操作。</summary>
    public InventoryDocumentLifecycleStatusEnum LifecycleStatus { get; set; }
    /// <summary>库存过账状态，用于判断当前记录允许执行的业务操作。</summary>
    public OutboundPostingStatusEnum PostingStatus { get; set; }
    /// <summary>关联的仓库站点唯一标识。</summary>
    public Guid WarehouseSiteId { get; set; }
    /// <summary>单据或业务事实形成时冻结的仓库编码历史快照，不随主数据更新。</summary>
    public string? WarehouseCodeSnapshot { get; set; }
    /// <summary>单据或业务事实形成时冻结的仓库名称历史快照，不随主数据更新。</summary>
    public string? WarehouseNameSnapshot { get; set; }
    /// <summary>该实体保存的实际出货日期。</summary>
    public DateTime ShipmentDate { get; set; }
    /// <summary>人工库存出库备注，最多 500 字。</summary>
    public string? Remark { get; set; }
    /// <summary>触发自动出库的装箱单标识；手工出库为空，不代表销售交付来源。</summary>
    public Guid? TriggerPackingListId { get; set; }
    /// <summary>单据或业务事实形成时冻结的装箱单号历史快照，不随主数据更新。</summary>
    public string? PackingListNoSnapshot { get; set; }
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
    /// <summary>业务命令唯一标识，用于保障对应业务命令幂等执行。</summary>
    public Guid CommandId { get; set; }
}
