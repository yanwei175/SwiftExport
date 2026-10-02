using System;

namespace SwiftExport.Contracts.DTOs.Products
{
    /// <summary>
    /// 产品搜索选择器使用的轻量只读模型。
    /// </summary>
    /// <remarks>来源表：Product、ProductMaster、ProductCategory、ProductColor；CustomerProduct 仅参与搜索匹配。</remarks>
    public class ProductLookupDto
    {
        public Guid Uid { get; set; }
        public string Sku { get; set; }
        public string DescriptionEn { get; set; }
        public string DescriptionZh { get; set; }
        public string LinkSku { get; set; }
        /// <remarks>来源：ProductCategory.CategoryName；经 Product.ProductMasterId 和 ProductMaster.CategoryId 关联。</remarks>
        public string CategoryName { get; set; }
        public string MainImage { get; set; }
        /// <remarks>来源：ProductColor.ColorNameZh；关联 Product.ColorId = ProductColor.Uid。</remarks>
        public string Finish { get; set; }
        public string FeaturesEn { get; set; }
        public string FeaturesZh { get; set; }
        public string MaterialType { get; set; }
        /// <summary>当前产品单位，仅用于显式选择产品时建立新快照；历史未知单位不得据此回填。</summary>
        public string Unit { get; set; }
        public decimal? USDPrice { get; set; }
        public decimal? CNYPrice { get; set; }
    }
}
