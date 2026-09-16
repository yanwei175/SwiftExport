using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;
namespace SwiftExport.Core.Entities.Procurement;
/// <summary>按完整路径保存确认 BOM 节点和客供件消耗目标，保留原始比例供审计与后续执行。</summary>
[Table("PurchaseConfirmationNode")]
public sealed partial class PurchaseConfirmationNode : BaseEntity
{
    /// <summary>所属采购确认行依据标识。</summary>
    public Guid ConfirmationLineId { get; set; }
    /// <summary>节点在确认 BOM 树中的稳定完整路径。</summary>
    public string Path { get; set; } = "";
    /// <summary>确认 BOM 树的根产品标识。</summary>
    public Guid RootProductId { get; set; }
    /// <summary>本节点直接父级产品标识。</summary>
    public Guid ParentProductId { get; set; }
    /// <summary>本节点组件产品标识。</summary>
    public Guid ComponentProductId { get; set; }
    /// <summary>形成该节点的 BOM 行标识。</summary>
    public Guid BomLineId { get; set; }
    /// <summary>父级产品 BOM 编号快照。</summary>
    public string ParentBomNo { get; set; } = "";
    /// <summary>父级产品 BOM 版本快照。</summary>
    public int ParentBomVersion { get; set; }
    /// <summary>父级产品 SKU 快照。</summary>
    public string ParentSkuSnapshot { get; set; } = "";
    /// <summary>组件产品 SKU 快照。</summary>
    public string ComponentSkuSnapshot { get; set; } = "";
    /// <summary>组件计量单位快照。</summary>
    public string UnitSnapshot { get; set; } = "";
    /// <summary>组件物料类型快照。</summary>
    public string MaterialType { get; set; } = "";
    /// <summary>组件供料类型快照。</summary>
    public string SupplyType { get; set; } = "";
    /// <summary>当前 BOM 层级用量比例的分子。</summary>
    public int Numerator { get; set; }
    /// <summary>当前 BOM 层级用量比例的分母。</summary>
    public int Denominator { get; set; }
    /// <summary>从根产品累计到本节点的用量比例。</summary>
    public decimal CumulativeRatio { get; set; }
    /// <summary>本节点是否为需要在履约时扣减的客供件。</summary>
    public bool IsCustomerSupplied { get; set; }
    /// <summary>客供件消耗仓库标识；非客供件或尚未指定时为空。</summary>
    public Guid? ConsumptionWarehouseSiteId { get; set; }
    /// <summary>客供件消耗仓库编码快照。</summary>
    public string? WarehouseCodeSnapshot { get; set; }
    /// <summary>客供件消耗仓库名称快照。</summary>
    public string? WarehouseNameSnapshot { get; set; }
    /// <summary>客供件消耗所关联的供应商标识。</summary>
    public Guid? ConsumptionSupplierId { get; set; }
    /// <summary>客供件供应商编码快照。</summary>
    public string? SupplierCodeSnapshot { get; set; }
    /// <summary>客供件供应商名称快照。</summary>
    public string? SupplierNameSnapshot { get; set; }
}
