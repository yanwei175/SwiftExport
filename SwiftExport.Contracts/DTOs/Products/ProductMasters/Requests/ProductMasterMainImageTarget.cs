using System;

namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests
{
    /// <summary>标识本次补齐默认主图的主型号和用户已读取的版本。</summary>
    public class ProductMasterMainImageTarget
    {
        /// <summary>获取或设置主型号身份。</summary>
        public Guid ProductMasterId { get; set; }
        /// <summary>获取或设置主型号预期版本。</summary>
        public int ExpectedVersion { get; set; }
    }
}
