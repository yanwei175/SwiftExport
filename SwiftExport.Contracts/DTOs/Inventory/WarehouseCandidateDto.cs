using System;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Contracts.DTOs.Inventory
{
    /// <summary>采购收货或消耗仓库新增候选；显示当前资料，不代表已保存快照或最终准入。</summary>
    public class WarehouseCandidateDto
    {
        /// <summary>仓库稳定身份，供采购命令引用。</summary>
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        /// <summary>当前仅返回供应商虚拟仓。</summary>
        public WarehouseSiteTypeEnum SiteType { get; set; }
        public bool IsActive { get; set; }
        /// <summary>仓库所属供应商身份，不要求等于本次采购的实际供货供应商。</summary>
        public Guid SupplierId { get; set; }
        public string SupplierCode { get; set; }
        public string SupplierName { get; set; }
        /// <summary>所属供应商当前启用资格；与仓库启用分别表达，候选中均为真。</summary>
        public bool SupplierIsActive { get; set; }
    }
}
