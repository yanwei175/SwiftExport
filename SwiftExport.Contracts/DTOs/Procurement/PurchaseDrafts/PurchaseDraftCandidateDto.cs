using SwiftExport.Contracts.SysEnums;
using System;

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
        public string Unit { get; set; }
        public string SupplyType { get; set; }
        public Guid? SuggestedPurchaseSupplierId { get; set; }
        public string SuggestedPurchaseSupplierCode { get; set; }
        public string SuggestedPurchaseSupplierName { get; set; }
        public Guid? SuggestedDeliverySupplierId { get; set; }
        public string SuggestedDeliverySupplierCode { get; set; }
        public string SuggestedDeliverySupplierName { get; set; }
        public string SuggestedDeliverySupplierHint { get; set; }
        public decimal GrossQty { get; set; }
        public decimal NetQty { get; set; }
        public DateTime? DemandRequiredDate { get; set; }
        public decimal AssociatedPurchaseQty { get; set; }
        /// <summary>当前尚未被有效采购贡献承接的数量。</summary>
        public decimal RemainingQty { get { return Math.Max(0m, NetQty - AssociatedPurchaseQty); } }
        public string PurchaseOrderNos { get; set; }
    }
}
