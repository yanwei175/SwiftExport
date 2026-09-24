using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Inventory
{
    /// <summary>筛选库存余额列表。</summary>
    public class InventoryBalanceSearchRequest : PagedRequestBase
    {
        public Guid? SupplierId { get; set; }
        public Guid? WarehouseSiteId { get; set; }
        public bool? NonZeroOnly { get; set; }
    }
}
