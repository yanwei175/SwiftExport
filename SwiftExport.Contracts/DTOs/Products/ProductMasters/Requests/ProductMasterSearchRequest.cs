using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests
{
    /// <summary>主型号分页查询条件。</summary>
    public class ProductMasterSearchRequest : PagedRequestBase
    {
        /// <summary>获取或设置允许命中的分类及后代分类标识。</summary>
        public List<Guid> CategoryIds { get; set; } = new List<Guid>();
    }
}
