using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Customers.Requests
{
    public class CustomerProductSearchRequest : PagedRequestBase
    {
        public List<string> BatchKeywords { get; set; } = new List<string>();
        public string ColorCodeUnique { get; set; } // 按颜色代码筛选
        public string UserTag { get; set; } // 按用户标签筛选
        public Guid CustomerId { get; set; } // 按客户id筛选

        public List<Guid> CustomerIds { get; set; } // 按客户id多选
        public List<string> CustomerCodes { get; set; } // 按客户代码筛选

        public string CategoryName { get; set; } // 按分类名称筛选
        public Guid CategoryId { get; set; } // 按分类id单选
        public List<Guid> CategoryIds { get; set; } // 按分类id多选

        public string MaterialType { get; set; } //物料类型
    }
}
