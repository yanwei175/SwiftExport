using SwiftExport.Contracts.DTOs.Base;


namespace SwiftExport.Contracts.DTOs.Products
{
    //挑选了UI 最可能用到的字段。不需要把 Entity 的几十个字段全搬过来。
    public class ProductDto : BaseDto
    {
        // ERP SKU级别管理字段
        public string MasterSku { get; set; }
        public string Sku { get; set; }
        public string DescriptionEn { get; set; } 
        public string DescriptionZh { get; set; }
        public string FeaturesEn { get; set; } 
        public string FeaturesZh { get; set; } 
        public string Finish { get; set; }
        public string MaterialType { get; set; }
        public string SupplyType { get; set; }//供货类型,直接采购,拆BOM,客供件之类,分解BOM依赖字段.
        public string BomNo { get; set; }
        public string Note { get; set; }
        public string LinkSku { get; set; }

        // 价格
        public decimal CNYPrice { get; set; }
        public decimal USDPrice { get; set; }


        // 装箱基础数据（供销售订单行带出）
        public decimal? PcsOfCarton { get; set; }
        public decimal? NwOfCarton { get; set; }
        public decimal? GwOfCarton { get; set; }
        public decimal? CbmOfCarton { get; set; }

        // 聚合信息 (由后端 SQL Join 查好填进来，UI 直接显示)
        public int CategoryId { get; set; }
        public int ColorId { get; set; }

        public string CategoryName { get; set; }
        public string SupplierName { get; set; }

        // 你的旧系统特定字段 (用于兼容 Excel 导入导出)
        public string CustomerCode { get; set; }
        public string CSku { get; set; }
        public string SupplierCode { get; set; }
        public string SSku { get; set; }

        // 图片路径 (如果有)
        public string MainImage { get; set; }
    }
}