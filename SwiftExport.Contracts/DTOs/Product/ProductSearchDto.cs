namespace SwiftExport.Contracts.DTOs.Product
{

    //用于前端发请求给后端。
    public class ProductSearchDto
    {
        public string Keyword { get; set; } // 搜索 SKU 或 描述
        public string CustomerCode { get; set; } // 按客户筛选
        public int CategoryId { get; set; } // 按分类筛选

        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }
}