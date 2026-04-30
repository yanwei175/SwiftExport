using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Products
{

    //用于前端发请求给后端。
    public class ProductSearchDto
    {
        public string Keyword { get; set; } // 搜索 SKU 或 描述

        public string ColorCodeUnique { get; set; } // 按颜色代码筛选
        public string UserTag { get; set; } // 按用户标签筛选
        public int CustomerId { get; set; } // 按客户id筛选

        public List<int> CustomerIds { get; set; } // 按客户id多选
        public List<string> CustomerCodes { get; set; } // 按客户代码筛选

        public string CategoryName { get; set; } // 按分类名称筛选
        public int CategoryId { get; set; } // 按分类id单选
        public List<int> CategoryIds { get; set; } // 按分类id多选

        public string MaterialType { get; set; } //物料类型

        public string BatchKeywords { get; set; }//p批量搜索
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 20;

    }
}