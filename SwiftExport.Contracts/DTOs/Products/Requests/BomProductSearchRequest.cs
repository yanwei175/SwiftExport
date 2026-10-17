using System;
using System.Collections.Generic;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Products.Requests
{
    /// <summary>查询 BOM 管理成品列表；绑定条件为空表示全部，分类与颜色均支持多选。</summary>
    public class BomProductSearchRequest : PagedRequestBase
    {
        public bool? IsBound { get; set; }
        /// <summary>为 true 时仅返回当前有效 BOM 子件数大于零的成品；false 或省略不限制子件数，计数与分页使用同一条件。</summary>
        public bool HasComponents { get; set; }
        public List<Guid> CategoryIds { get; set; } = new List<Guid>();
        public List<Guid> ColorIds { get; set; } = new List<Guid>();
    }
}
