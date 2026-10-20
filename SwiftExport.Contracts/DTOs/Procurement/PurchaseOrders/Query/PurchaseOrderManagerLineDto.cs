using System;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto
{
    /// <summary>采购行事实与基本需求关联摘要；不承载履约进度，图片是 Product.MainImage 当前投影。</summary>
    public sealed class PurchaseOrderManagerLineDto
    {
        public Guid PurchaseOrderLineId { get; set; }
        public int Version { get; set; }
        public Guid ProductId { get; set; }
        public int LineNumber { get; set; }
        public string MainImage { get; set; }
        public string CustomerSku { get; set; }
        public string Sku { get; set; }
        public string LinkSku { get; set; }
        public string SupplierSku { get; set; }
        public string DescriptionZh { get; set; }
        public string FinishZh { get; set; }
        public string FeaturesZh { get; set; }
        public string MaterialType { get; set; }
        public string SupplyType { get; set; }
        public decimal Quantity { get; set; }
        public string UnitSnapshot { get; set; }
        /// <summary>通过引用身份、状态和数量校验的有效需求贡献合计；不能核实时为空。</summary>
        public decimal? LinkedDemandQty { get; set; }
        /// <summary>采购行尚未关联需求的数量；缺少依据或关联不守恒时为空。</summary>
        public decimal? UnlinkedQty { get; set; }
        /// <summary>活动且仍有贡献的引用所涉及的不重复物料需求数量，不表示采购行数量。</summary>
        public int LinkedDemandCount { get; set; }
        /// <summary>只读关联摘要状态，不改变采购确认或单据生命周期。</summary>
        public PurchaseDemandAssociationStatusEnum AssociationStatus { get; set; }
        /// <summary>关联身份、活动依据或数量守恒的核实原因；无异常时为空。</summary>
        public string AssociationReviewReason { get; set; }
        /// <summary>依据完整贡献关系生成的来源摘要；多需求不得用一个代表性来源行替代。</summary>
        public string SourceSummary { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? TaxPrice { get; set; }
        public decimal? UnitAmount { get; set; }
        public decimal? TaxAmount { get; set; }
        public string DeliverySupplierName { get; set; }
        public string SourceType { get; set; }
        public string SourceNo { get; set; }
        public int? SourceLineNumber { get; set; }
        public string SubNote { get; set; }
    }
}
