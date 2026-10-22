using System;

namespace SwiftExport.Contracts.DTOs.Inventory
{
    /// <summary>入库来源及库存查询的供应商筛选身份；包含停用但未删除的供应商，不表示当前可执行采购或过账。</summary>
    public class InventorySupplierLookupDto
    {
        /// <summary>筛选所用的供应商稳定标识。</summary>
        public Guid Uid { get; set; }

        /// <summary>供应商业务编码。</summary>
        public string SupplierCode { get; set; }

        /// <summary>供应商显示名称。</summary>
        public string SupplierName { get; set; }
    }
}
