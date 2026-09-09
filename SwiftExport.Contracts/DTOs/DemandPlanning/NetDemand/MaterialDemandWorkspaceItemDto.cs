using SwiftExport.Contracts.SysEnums;
using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand
{
    /// <summary>
    /// 物料需求工作台只读行，仅承载物料事实、来源快照和有效采购关联汇总。
    /// </summary>
    /// <remarks>
    /// 来源表为 NetDemand、GrossDemand、Product、DemandExecutionLink、PurchaseOrderHead 和 PurchaseOrderLine。
    /// 本契约不得读取 NetDemandAllocation；计划建议、供应商和送达目标待责任领域确认后通过独立只读查询补回。
    /// </remarks>
    public class MaterialDemandWorkspaceItemDto
    {
        /// <summary>物料需求 Uid。</summary>
        public Guid Uid { get; set; }
        /// <summary>物料需求并发版本。</summary>
        public int Version { get; set; }
        /// <summary>物料需求生命周期。</summary>
        public NetDemandLifecycleStatusEnum LifecycleStatus { get; set; }
        /// <summary>所属来源需求 Uid。</summary>
        public Guid GrossDemandId { get; set; }
        /// <summary>来源需求拆解修订号。</summary>
        public int DecompositionRevision { get; set; }
        /// <summary>拆解修订内的物料行号。</summary>
        public int LineNumber { get; set; }
        /// <summary>来源业务类型快照。</summary>
        public string SourceType { get; set; }
        /// <summary>来源业务单据 Uid。</summary>
        public Guid SourceUid { get; set; }
        /// <summary>来源业务单号快照。</summary>
        public string SourceNo { get; set; }
        /// <summary>来源业务行号快照。</summary>
        public int? SourceLineNumber { get; set; }
        /// <summary>拆解树根物料需求 Uid。</summary>
        public Guid RootNetDemandId { get; set; }
        /// <summary>拆解树根 SKU 快照。</summary>
        public string RootSku { get; set; }
        /// <summary>是否为送往父件执行供应商的客供件。</summary>
        public bool IsCustomerSupplied { get; set; }
        /// <summary>关联产品 Uid；非标物料允许为空。</summary>
        public Guid? ProductId { get; set; }
        /// <summary>是否为未关联产品的非标物料。</summary>
        public bool IsNonStandard { get; set; }
        /// <summary>物料 SKU 快照。</summary>
        public string Sku { get; set; }
        /// <summary>产品关联 SKU。</summary>
        public string LinkSku { get; set; }
        /// <summary>客户型号快照。</summary>
        public string CustomerSku { get; set; }
        /// <summary>物料描述快照。</summary>
        public string Description { get; set; }
        /// <summary>颜色快照。</summary>
        public string Color { get; set; }
        /// <summary>规格快照。</summary>
        public string Specification { get; set; }
        /// <summary>产品图片路径快照。</summary>
        public string ImagePath { get; set; }
        /// <summary>物料类型快照。</summary>
        public string MaterialType { get; set; }
        /// <summary>计量单位。</summary>
        public string Unit { get; set; }
        /// <summary>拆解时的供货方式快照。</summary>
        public string SupplyType { get; set; }
        /// <summary>BOM 展开后的原始需求数量。</summary>
        public decimal GrossQty { get; set; }
        /// <summary>当前净需求数量；现阶段未自动抵扣库存和在途。</summary>
        public decimal NetQty { get; set; }
        /// <summary>来源需求交期。</summary>
        public DateTime? DemandRequiredDate { get; set; }
        /// <summary>有效采购 Link 的分配量减释放量合计。</summary>
        public decimal AssociatedPurchaseQty { get; set; }
        /// <summary>有效采购 Link 对应的采购单号集合。</summary>
        public string PurchaseOrderNos { get; set; }
    }
}
