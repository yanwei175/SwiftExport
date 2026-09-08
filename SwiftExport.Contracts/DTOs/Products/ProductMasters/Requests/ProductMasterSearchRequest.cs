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

        /// <summary>获取或设置是否同时查询未设置分类的主型号。</summary>
        public bool IncludeUncategorized { get; set; }

        /// <summary>获取或设置需要排除的分类；空 Guid 表示不排除。</summary>
        public Guid ExcludeCategoryId { get; set; }
    }
}
