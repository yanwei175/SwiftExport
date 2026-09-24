using SwiftExport.Contracts.SysEnums.Inventory;
using System;

namespace SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker
{
    /// <summary>仓库选择器返回的只读候选；只表达仓库身份与显示资料，不代表下游业务授权。</summary>
    public class WarehouseSitePickerDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public Guid? SupplierId { get; set; }
        public string SupplierCode { get; set; }
        public string SupplierName { get; set; }
        public WarehouseSiteTypeEnum SiteType { get; set; }
        public bool IsActive { get; set; }
    }
}
