using SwiftExport.Contracts.SysEnums;
using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand
{
    /// <summary>
    /// 物料需求工作台只读行，仅承载物料事实、来源快照和有效采购关联汇总。
    /// </summary>
    /// <remarks>
    /// 来源表为 NetDemand、GrossDemand、Product、DemandExecutionLink、PurchaseOrderHead 和 PurchaseOrderLine。
    /// 本契约不得读取 NetDemandAllocation；推荐采购供应商来自 NetDemand，建议送达供应商由同树目标物料派生。
    /// 两者都不是采购域保存的实际采购方或送达方。
    /// </remarks>
    public class MaterialDemandManagerItemDto
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
        /// <summary>来源需求归属客户 Uid；采购单头客户快照只能由该权威事实派生。</summary>
        public Guid? CustomerId { get; set; }
        /// <summary>来源需求归属客户当前编码。</summary>
        public string CustomerCode { get; set; }
        /// <summary>来源需求归属客户当前名称。</summary>
        public string CustomerName { get; set; }
        /// <summary>来源业务类型快照。</summary>
        public string SourceType { get; set; }
        /// <summary>来源业务单据 Uid。</summary>
        public Guid SourceUid { get; set; }
        /// <summary>来源业务单号快照。</summary>
        public string SourceNo { get; set; }
        /// <summary>来源业务行号快照。</summary>
        public int? SourceLineNumber { get; set; }
        /// <summary>来源业务行 Uid，用于采购行历史追溯。</summary>
        public Guid SourceLineId { get; set; }
        /// <summary>拆解树根物料需求 Uid。</summary>
        public Guid RootNetDemandId { get; set; }
        /// <summary>拆解树根 SKU 快照。</summary>
        public string RootSku { get; set; }
        /// <summary>是否为送往父件执行供应商的客供件。</summary>
        public bool IsCustomerSupplied { get; set; }
        /// <summary>客供件的送达目标物料需求 Uid；标准交付为空。</summary>
        public Guid? DeliveryTargetNetDemandId { get; set; }
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
        /// <summary>物料需求保存的推荐采购供应商 Uid；仅为建议。</summary>
        public Guid? SuggestedPurchaseSupplierId { get; set; }
        /// <summary>推荐采购供应商当前编码。</summary>
        public string SuggestedPurchaseSupplierCode { get; set; }
        /// <summary>推荐采购供应商当前名称。</summary>
        public string SuggestedPurchaseSupplierName { get; set; }
        /// <summary>客供件按同一有效拆解树的送达目标派生出的建议送达供应商 Uid。</summary>
        public Guid? SuggestedDeliverySupplierId { get; set; }
        /// <summary>建议送达供应商当前编码；标准交付或未确定时为空。</summary>
        public string SuggestedDeliverySupplierCode { get; set; }
        /// <summary>建议送达供应商当前名称；标准交付或未确定时为空。</summary>
        public string SuggestedDeliverySupplierName { get; set; }
        /// <summary>客供件无法派生建议送达供应商时的轻量提示；标准交付为空。</summary>
        public string SuggestedDeliverySupplierHint { get; set; }
        /// <summary>推荐采购供应商与当前产品关系中的供应商型号。</summary>
        public string SuggestedSupplierSku { get; set; }
        /// <summary>推荐采购供应商关系中的未税价；不存在时为空。</summary>
        public decimal? SuggestedUnitPrice { get; set; }
        /// <summary>推荐采购供应商关系中的含税价；不存在时为空。</summary>
        public decimal? SuggestedTaxPrice { get; set; }
        /// <summary>BOM 展开后的原始需求数量。</summary>
        public decimal GrossQty { get; set; }
        /// <summary>当前净需求数量；现阶段未自动抵扣库存和在途。</summary>
        public decimal NetQty { get; set; }
        /// <summary>来源需求交期。</summary>
        public DateTime? DemandRequiredDate { get; set; }
        /// <summary>有效采购 Link 的分配量减释放量合计。</summary>
        public decimal AssociatedPurchaseQty { get; set; }
        /// <summary>当前尚未被有效采购 Link 承接的数量。</summary>
        public decimal RemainingQty { get { return Math.Max(0m, NetQty - AssociatedPurchaseQty); } }
        /// <summary>有效采购 Link 对应的采购单号集合。</summary>
        public string PurchaseOrderNos { get; set; }
    }
}
