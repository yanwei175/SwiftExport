using SwiftExport.Core.Common;
using System.Collections.Generic;

namespace SwiftExport.AppLayer.DTOs.OtherDTOs
{
    public class ProductQueryInput
    {
        // 直接复用 Core 层的通用类
        public Pagination Pager { get; set; } = new Pagination();
        public string GlobalSearchKey { get; set; }
        public Dictionary<string, QueryCondition> FilterConditions { get; set; }
            = new Dictionary<string, QueryCondition>();
    }
}
