using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>采购从物料需求取数的只读候选，完整保留预览、追溯和页面识别所需事实。</summary>
    public class PurchaseDraftCandidateDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public NetDemandLifecycleStatusEnum LifecycleStatus { get; set; }
        public Guid GrossDemandId { get; set; }
        public int DecompositionRevision { get; set; }
        public int LineNumber { get; set; }
        public Guid? CustomerId { get; set; }
        public string CustomerCode { get; set; }
        public string CustomerName { get; set; }
        public string SourceType { get; set; }
        public Guid SourceUid { get; set; }
        public string SourceNo { get; set; }
        public int? SourceLineNumber { get; set; }
        public Guid SourceLineId { get; set; }
        public Guid RootNetDemandId { get; set; }
        public string RootSku { get; set; }
        public bool IsCustomerSupplied { get; set; }
        public Guid? DeliveryTargetNetDemandId { get; set; }
        public Guid? ProductId { get; set; }
        public bool IsNonStandard { get; set; }
        public string Sku { get; set; }
        public string LinkSku { get; set; }
        public string CustomerSku { get; set; }
        public string Description { get; set; }
        public string Color { get; set; }
        public string Specification { get; set; }
        public string ImagePath { get; set; }
        public string MaterialType { get; set; }
        /// <summary>物料需求建立时保存的单位快照；未知为空，不从当前产品推断。</summary>
        public string Unit { get; set; }
        public string SupplyType { get; set; }
        public Guid? SuggestedPurchaseSupplierId { get; set; }
        public string SuggestedPurchaseSupplierCode { get; set; }
        public string SuggestedPurchaseSupplierName { get; set; }
        public Guid? SuggestedDeliverySupplierId { get; set; }
        public string SuggestedDeliverySupplierCode { get; set; }
        public string SuggestedDeliverySupplierName { get; set; }
        public string SuggestedDeliverySupplierHint { get; set; }
        public string SuggestedSupplierSku { get; set; }
        public decimal? SuggestedUnitPrice { get; set; }
        public decimal? SuggestedTaxPrice { get; set; }
        /// <summary>客供件父件活动采购单中的实际采购供应商；多项时必须由采购员明确选择送达方。</summary>
        public List<PurchaseDraftActualSupplierFactDto> ParentActualPurchaseSuppliers { get; set; } = new List<PurchaseDraftActualSupplierFactDto>();
        public decimal GrossQty { get; set; }
        public decimal NetQty { get; set; }
        public DateTime? DemandRequiredDate { get; set; }
        /// <summary>可信已确认采购关联量；无权或证据异常为空。</summary>
        public decimal? PurchasedQty { get; set; }
        /// <summary>可信未确认草稿的来源占用量，不计入已采购；无权或证据异常为空。</summary>
        public decimal? DraftOccupiedQty { get; set; }
        /// <summary>需求量减已采购量及草稿占用量；证据异常为空，不得按零放行。</summary>
        public decimal? CreatableQty { get; set; }
        /// <summary>需求量减已采购量；草稿不减少此数量，无权或证据异常为空。</summary>
        public decimal? UnpurchasedQty { get; set; }
        /// <summary>来源、占用目标或数量无法核实的具体原因；非空时不得创建或补关联。</summary>
        public string ReviewReason { get; set; }
        /// <summary>已采购与草稿占用的总关联量；证据异常为空。</summary>
        public decimal? AssociatedPurchaseQty { get; set; }
        /// <summary>当前尚未被有效采购贡献承接的数量。</summary>
        public decimal? RemainingQty { get { return AssociatedPurchaseQty.HasValue ? Math.Max(0m, NetQty - AssociatedPurchaseQty.Value) : (decimal?)null; } }
        public string PurchaseOrderNos { get; set; }
        /// <summary>当前占用该物料需求的草稿引用；无采购读取权限时不返回。</summary>
        public List<PurchaseDraftReferenceDto> DraftOrders { get; set; } = new List<PurchaseDraftReferenceDto>();
    }
}
