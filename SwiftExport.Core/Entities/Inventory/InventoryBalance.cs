using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Inventory;

/// <summary>库存余额实体，保存仓库与产品维度的当前库存投影。</summary>
[Table("InventoryBalance")]
public sealed class InventoryBalance : BaseEntity
{
    /// <summary>关联的仓库站点唯一标识。</summary>
    public Guid WarehouseSiteId { get; set; }
    /// <summary>关联的产品主数据唯一标识。</summary>
    public Guid ProductId { get; set; }
    /// <summary>单据或业务事实形成时冻结的产品编码历史快照，不随主数据更新。</summary>
    public string? SkuSnapshot { get; set; }
    /// <summary>单据或业务事实形成时冻结的计量单位历史快照，不随主数据更新。</summary>
    public string? UnitSnapshot { get; set; }
    /// <summary>当前仓库与产品维度的在手库存投影，由有效库存流水累计形成。</summary>
    public decimal OnHandQty { get; set; }
    /// <summary>该实体保存的最后入账流水序号。</summary>
    public long LastLedgerSequence { get; set; }
}
