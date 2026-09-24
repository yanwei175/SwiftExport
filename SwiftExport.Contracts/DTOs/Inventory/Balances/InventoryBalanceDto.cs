using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Inventory
{
    /// <remarks>来源表：InventoryBalance、WarehouseSite、Product。</remarks>
    /// <summary>返回库存余额及当前显示资料。</summary>
    public class InventoryBalanceDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public Guid WarehouseSiteId { get; set; }
        /// <remarks>来源：WarehouseSite.Code。</remarks>
        public string WarehouseCode { get; set; }
        /// <remarks>来源：WarehouseSite.Name。</remarks>
        public string WarehouseName { get; set; }
        public Guid? SupplierId { get; set; }
        public string SupplierCode { get; set; }
        public string SupplierName { get; set; }
        public Guid ProductId { get; set; }
        /// <remarks>来源：优先 Product.Sku，为空时使用 InventoryBalance.SkuSnapshot。</remarks>
        public string Sku { get; set; }
        /// <remarks>来源：优先 Product.DescriptionZh，其次 Product.DescriptionEn，最后使用产品编码。</remarks>
        public string ProductName { get; set; }
        /// <remarks>来源：InventoryBalance.UnitSnapshot。</remarks>
        public string Unit { get; set; }
        /// <remarks>来源：当前未删除 Product.MainImage；产品缺失或已删除时为空。</remarks>
        public string ImagePath { get; set; }
        public decimal OnHandQty { get; set; }
        /// <remarks>计算来源：当前阶段等于 InventoryBalance.OnHandQty，尚未扣减独立预占量。</remarks>
        public decimal AvailableQty { get; set; }
        public long LastLedgerSequence { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }
}
