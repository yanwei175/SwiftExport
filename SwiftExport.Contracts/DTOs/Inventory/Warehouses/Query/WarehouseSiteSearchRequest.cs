using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Inventory
{
    /// <summary>筛选供应商虚拟仓管理列表。</summary>
    public class WarehouseSiteSearchRequest : PagedRequestBase
    {
        public Guid? SupplierId { get; set; }
        public bool? IsActive { get; set; }
    }
}
