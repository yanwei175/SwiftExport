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
        /// <summary>未税单价，必须明确填写，允许零价，非负且最多四位小数；保存时由服务端重算未税金额。</summary>
        public decimal? UnitPrice { get; set; }
        /// <summary>含税单价，必须明确填写，允许零价，非负且最多四位小数；保存时由服务端重算含税金额。</summary>
        public decimal? TaxPrice { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
        public string SubNote { get; set; }
    }
}
