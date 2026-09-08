using System;

namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses
{
    /// <summary>描述一个已归类主型号的新旧版本。</summary>
    public class AssignedProductMasterVersionResult
    {
        /// <summary>获取或设置主型号标识。</summary>
        public Guid ProductMasterId { get; set; }

        /// <summary>获取或设置更新前版本。</summary>
        public int OldVersion { get; set; }

        /// <summary>获取或设置更新后版本。</summary>
        public int NewVersion { get; set; }
    }
}
