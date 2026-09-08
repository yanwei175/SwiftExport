using System;

namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests
{
    /// <summary>描述一个待归类主型号及其乐观并发版本。</summary>
    public class AssignProductMasterToCategoryItem
    {
        /// <summary>获取或设置主型号标识。</summary>
        public Guid ProductMasterId { get; set; }

        /// <summary>获取或设置客户端读取到的版本。</summary>
        public int ExpectedVersion { get; set; }
    }
}
