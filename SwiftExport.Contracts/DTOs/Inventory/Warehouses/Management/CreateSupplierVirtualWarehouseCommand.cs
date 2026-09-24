using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Inventory
{
    /// <summary>请求创建供应商虚拟仓。</summary>
    public class CreateSupplierVirtualWarehouseCommand
    {
        public Guid CommandId { get; set; }
        public Guid SupplierId { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
    }
}
