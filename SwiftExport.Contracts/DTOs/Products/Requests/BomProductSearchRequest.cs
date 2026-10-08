using System;
using System.Collections.Generic;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Products.Requests
{
    /// <summary>查询 BOM 管理成品列表；绑定条件为空表示全部，分类与颜色均支持多选。</summary>
    public class BomProductSearchRequest : PagedRequestBase
    {
        public bool? IsBound { get; set; }
        public List<Guid> CategoryIds { get; set; } = new List<Guid>();
        public List<Guid> ColorIds { get; set; } = new List<Guid>();
    }
}
