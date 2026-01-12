using SwiftExport.Contracts.DTOs.Base;


namespace SwiftExport.Contracts.DTOs.Product;

//挑选了UI 最可能用到的字段。不需要把 Entity 的几十个字段全搬过来。
public class ProductDto : BaseDto
{
    // 核心信息
    public string Sku { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // 价格
    public decimal RmbPrice { get; set; }
    public decimal UsdPrice { get; set; }

    // 聚合信息 (由后端 SQL Join 查好填进来，UI 直接显示)
    public string CategoryName { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;

    // 你的旧系统特定字段 (用于兼容 Excel 导入导出)
    public string CustomerCode { get; set; } = string.Empty;
    public string CSku { get; set; } = string.Empty;

    // 图片路径 (如果有)
    public string MainImagePath { get; set; } = string.Empty;
}