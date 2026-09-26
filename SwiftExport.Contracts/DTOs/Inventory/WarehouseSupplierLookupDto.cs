using System;

namespace SwiftExport.Contracts.DTOs.Inventory
{
    /// <summary>虚拟仓管理专用供应商候选，仅暴露精确选择所需的稳定身份、编码和名称。</summary>
    public class WarehouseSupplierLookupDto
    {
        /// <summary>供应商稳定标识。</summary>
        public Guid Uid { get; set; }

        /// <summary>供应商业务编码。</summary>
        public string SupplierCode { get; set; }

        /// <summary>供应商名称。</summary>
        public string SupplierName { get; set; }
    }
}
