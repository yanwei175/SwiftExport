using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Shipping;

/// <summary>装箱单来源贡献实体，逐行保存本次出货数量及其销售、需求、执行计划和仓库来源。</summary>
[Table("PackingListContributionLine")]
public sealed class PackingListContributionLine : BaseEntity
{
    /// <summary>关联的装箱单头唯一标识。</summary>
    public Guid PackingListDocumentId { get; set; }
    /// <summary>该实体保存的单据行号。</summary>
    public int LineNumber { get; set; }
    /// <summary>关联的产品主数据唯一标识。</summary>
    public Guid ProductId { get; set; }
    /// <summary>单据或业务事实形成时冻结的产品编码历史快照，不随主数据更新。</summary>
    public string SkuSnapshot { get; set; } = string.Empty;
    /// <summary>单据或业务事实形成时冻结的客户产品编码历史快照，不随主数据更新。</summary>
    public string? CustomerSkuSnapshot { get; set; }
    /// <summary>单据或业务事实形成时冻结的产品描述历史快照，不随主数据更新。</summary>
    public string? DescriptionSnapshot { get; set; }
    /// <summary>单据或业务事实形成时冻结的计量单位历史快照，不随主数据更新。</summary>
    public string? UnitSnapshot { get; set; }
    /// <summary>本次最终出货由该销售、需求或执行来源贡献的数量。</summary>
    public decimal Quantity { get; set; }
    /// <summary>该实体保存的包装件数。</summary>
    public decimal? PackageCount { get; set; }
    /// <summary>该实体保存的净重。</summary>
    public decimal? NetWeight { get; set; }
    /// <summary>该实体保存的毛重。</summary>
    public decimal? GrossWeight { get; set; }
    /// <summary>该实体保存的装箱体积。</summary>
    public decimal? Volume { get; set; }
    /// <summary>该实体保存的装箱唛头。</summary>
    public string? Marks { get; set; }
    /// <summary>该实体保存的业务备注。</summary>
    public string? Note { get; set; }
    /// <summary>关联的毛需求唯一标识。</summary>
    public Guid GrossDemandId { get; set; }
    /// <summary>可选的物料需求追溯标识；客户交付不以物料拆解为前提。</summary>
    public Guid? NetDemandId { get; set; }
    /// <summary>可选的 Allocation 追溯标识；客户交付不以计划分配为前提。</summary>
    public Guid? NetDemandAllocationId { get; set; }
    /// <summary>建立草稿时看到的来源需求并发版本，用于提交时检测预览过期。</summary>
    public int SourceDemandVersionSnapshot { get; set; }
    /// <summary>建立草稿时看到的上游来源事实版本。</summary>
    public int? SourceVersionSnapshot { get; set; }
    /// <summary>关联的需求执行追溯关系唯一标识。</summary>
    public Guid? DemandExecutionLinkId { get; set; }
    /// <summary>关联的销售订单头唯一标识。</summary>
    public Guid? SalesOrderId { get; set; }
    /// <summary>关联的销售订单明细唯一标识。</summary>
    public Guid? SalesOrderLineId { get; set; }
    /// <summary>单据或业务事实形成时冻结的销售订单号历史快照，不随主数据更新。</summary>
    public string? SalesOrderNoSnapshot { get; set; }
    /// <summary>单据或业务事实形成时冻结的销售订单行号历史快照，不随主数据更新。</summary>
    public int? SalesOrderLineNumberSnapshot { get; set; }
    /// <summary>关联的来源供应商唯一标识。</summary>
    public Guid? SourceSupplierId { get; set; }
    /// <summary>单据或业务事实形成时冻结的来源供应商名称历史快照，不随主数据更新。</summary>
    public string? SourceSupplierNameSnapshot { get; set; }
    /// <summary>关联的仓库站点唯一标识。</summary>
    public Guid WarehouseSiteId { get; set; }
    /// <summary>单据或业务事实形成时冻结的仓库编码历史快照，不随主数据更新。</summary>
    public string? WarehouseCodeSnapshot { get; set; }
    /// <summary>单据或业务事实形成时冻结的仓库名称历史快照，不随主数据更新。</summary>
    public string? WarehouseNameSnapshot { get; set; }
    /// <summary>该实体保存的需求交期。</summary>
    public DateTime? SnapshotRequiredDate { get; set; }
    /// <summary>该实体保存的供货方式。</summary>
    public byte SupplyType { get; set; }
    /// <summary>装箱贡献明细的幂等标识，保证同一来源贡献只处理一次。</summary>
    public Guid CommandItemId { get; set; }
}
