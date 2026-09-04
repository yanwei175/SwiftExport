using System;

namespace SwiftExport.Contracts.DTOs.Products.ProductMasters
{
    /// <summary>中性产品可写字段；父主型号由详情保存上下文确定。</summary>
    public class ProductNeutralWriteDto
    {
        /// <summary>获取或设置中性产品编码。</summary>
        public string ProductCode { get; set; }
        /// <summary>获取或设置颜色标识。</summary>
        public Guid ColorId { get; set; }
        /// <summary>获取或设置默认美元销售价。</summary>
        public decimal? DefaultSalesPriceUsd { get; set; }
        /// <summary>获取或设置默认汇率。</summary>
        public decimal? DefaultExchangeRate { get; set; }
        /// <summary>获取或设置利润率；本期不自动计算。</summary>
        public decimal? ProfitMargin { get; set; }
        /// <summary>获取或设置默认人民币采购价。</summary>
        public decimal? DefaultPurchasePriceCny { get; set; }
        /// <summary>获取或设置图片路径。</summary>
        public string ImagePath { get; set; }
        /// <summary>获取或设置当前图片是否作为所属主型号的主图。</summary>
        public bool IsMainImage { get; set; }
    }
}
