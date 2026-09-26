using System;
using System.Collections.Generic;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Requests
{
    /// <summary>中性产品管理页分页查询条件。</summary>
    public class ProductNeutralManagerSearchRequest : PagedRequestBase
    {
        /// <summary>获取或设置允许命中的分类及其后代分类标识。</summary>
        public List<Guid> CategoryIds { get; set; } = new List<Guid>();
        /// <summary>获取或设置是否仅查询未设置分类的主型号所属中性产品。</summary>
        public bool IncludeUncategorized { get; set; }
        /// <summary>获取或设置允许命中的颜色标识；空集合表示全部颜色。</summary>
        public List<Guid> ColorIds { get; set; } = new List<Guid>();
    }
}
