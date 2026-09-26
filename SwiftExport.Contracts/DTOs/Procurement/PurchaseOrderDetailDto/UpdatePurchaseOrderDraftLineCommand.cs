using System;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto
{
    /// <summary>更新既有采购行的商务白名单；身份、数量、单位、目标和来源不可由该命令修改。</summary>
    public sealed class UpdatePurchaseOrderDraftLineCommand
    {
        public Guid PurchaseOrderLineId { get; set; }
        public int ExpectedVersion { get; set; }
        public string SupplierSku { get; set; }
        public string DescriptionZh { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? TaxPrice { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
        public string SubNote { get; set; }
    }
}
