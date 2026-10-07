using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Common;
using SwiftExport.Core.DatabaseSchema;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Inventory;

/// <summary>库存流水实体，保存每次库存增减及反向的不可变业务事实。</summary>
[Table("inventory_ledger_entry")]
public sealed partial class InventoryLedgerEntry : BaseEntity
{
    /// <summary>该实体保存的业务流水顺序号。</summary>
    [DbSequence("InventoryLedgerSequence")]
    public long Sequence { get; set; }
    /// <summary>关联的仓库站点唯一标识。</summary>
    public Guid WarehouseSiteId { get; set; }
    /// <summary>关联的产品主数据唯一标识。</summary>
    public Guid ProductId { get; set; }
    /// <summary>该实体保存的库存交易类型。</summary>
    public InventoryTransactionTypeEnum TransactionType { get; set; }
    /// <summary>本次库存变动量；入库为正、出库为负，反向业务按相反方向记录。</summary>
    public decimal QuantityDelta { get; set; }
    /// <summary>本次流水入账后的库存余额，用于按序核对库存守恒。</summary>
    public decimal BalanceAfter { get; set; }
    /// <summary>该实体保存的来源业务单据类型。</summary>
    public string SourceDocumentType { get; set; } = string.Empty;
    /// <summary>关联的来源业务单据唯一标识。</summary>
    public Guid? SourceDocumentId { get; set; }
    /// <summary>关联的来源业务明细唯一标识。</summary>
    public Guid? SourceLineId { get; set; }
    /// <summary>来源业务单据形成库存事实时的真实编号快照；来源本身无编号时允许为空。</summary>
    public string? SourceDocumentNoSnapshot { get; set; }
    /// <summary>关联的被反向的原库存流水唯一标识。</summary>
    public Guid? ReversalOfLedgerId { get; set; }
    /// <summary>纠错替代事实所替代的原库存流水；普通过账不得设置。</summary>
    public Guid? ReplacementOfLedgerId { get; set; }
    /// <summary>原子纠错组的稳定标识；组内反向与替代事实共用同一标识。</summary>
    public Guid? CorrectionGroupId { get; set; }
    /// <summary>产生本次库存业务的命令唯一标识。</summary>
    public Guid CommandId { get; set; }
    /// <summary>本次库存变动明细的幂等标识，保证同一明细只入账一次。</summary>
    public Guid CommandItemId { get; set; }
    /// <summary>规范化原始请求的 SHA-256 指纹，保留缺省与显式快照的差异并支持可靠重放。</summary>
    public string? RequestFingerprint { get; set; }
    /// <summary>该实体保存的业务操作原因。</summary>
    public string? Reason { get; set; }
    /// <summary>该实体保存的业务事实发生时间。</summary>
    public DateTime OccurredDate { get; set; }
    /// <summary>单据或业务事实形成时冻结的仓库编码历史快照，不随主数据更新。</summary>
    public string? WarehouseCodeSnapshot { get; set; }
    /// <summary>库存事实发生时的仓库名称快照。</summary>
    [DbColumn(StoreType = "varchar(300)")]
    public string? WarehouseNameSnapshot { get; set; }
    /// <summary>单据或业务事实形成时冻结的产品编码历史快照，不随主数据更新。</summary>
    public string? SkuSnapshot { get; set; }
    /// <summary>库存事实发生时的产品名称快照。</summary>
    public string? ProductNameSnapshot { get; set; }
    /// <summary>单据或业务事实形成时冻结的计量单位历史快照，不随主数据更新。</summary>
    public string? UnitSnapshot { get; set; }
}
