using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Inventory
{
    /// <remarks>来源表：WarehouseSite、Supplier。</remarks>
    /// <summary>返回仓库当前资料与供应商显示快照。</summary>
    public class WarehouseSiteDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public Guid? SupplierId { get; set; }
        /// <remarks>来源：Supplier.SupplierCode；关联 WarehouseSite.SupplierId = Supplier.Uid。</remarks>
        public string SupplierCode { get; set; }
        /// <remarks>来源：Supplier.SupplierName；关联 WarehouseSite.SupplierId = Supplier.Uid。</remarks>
        public string SupplierName { get; set; }
        public bool IsActive { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }
}
