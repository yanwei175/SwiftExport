using SwiftExport.Contracts.DTOs.Base;


namespace SwiftExport.Contracts.DTOs.Products
{
    //挑选了UI 最可能用到的字段。不需要把 Entity 的几十个字段全搬过来。
    public class ProductDto : BaseDto
    {
        // 核心信息
        public string Sku { get; set; }
        public string Description { get; set; }
        public string Finish { get; set; }
        public string Features { get; set; }

        // 价格
        public decimal RmbPrice { get; set; }
        public decimal UsdPrice { get; set; }

        // 聚合信息 (由后端 SQL Join 查好填进来，UI 直接显示)
        public string CategoryName { get; set; }
        public string SupplierName { get; set; }

        // 你的旧系统特定字段 (用于兼容 Excel 导入导出)
        public string CustomerCode { get; set; }
        public string CSku { get; set; }

        // 图片路径 (如果有)
        public string MainImage { get; set; }
    }
}