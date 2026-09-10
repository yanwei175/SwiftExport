using System;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>父件活动采购单提供的实际采购供应商事实，仅用于客供件送达方判断与展示。</summary>
    public class PurchaseDraftActualSupplierFactDto
    {
        /// <summary>实际采购供应商稳定身份。</summary>
        public Guid SupplierId { get; set; }

        /// <summary>活动采购单保存的实际供应商编码快照。</summary>
        public string SupplierCode { get; set; }

        /// <summary>活动采购单保存的实际供应商名称快照。</summary>
        public string SupplierName { get; set; }

        /// <summary>该供应商当前是否仍可用于新的采购选择。</summary>
        public bool IsActive { get; set; }

        /// <summary>证明该实际采购方的活动采购单号集合。</summary>
        public string PurchaseOrderNos { get; set; }
    }
}
