using System;

namespace SwiftExport.Contracts.DTOs.Products.ProductNeutralManager
{
    /// <summary>中性产品管理页可写字段，允许显式调整所属主型号。</summary>
    public class ProductNeutralManagerWriteDto
    {
        /// <summary>获取或设置所属主型号标识。</summary>
        public Guid ProductMasterId { get; set; }
        /// <summary>获取或设置中性产品业务编码。</summary>
        public string ProductCode { get; set; }
        /// <summary>获取或设置颜色标识。</summary>
        public Guid ColorId { get; set; }
        /// <summary>获取或设置默认美元销售价。</summary>
        public decimal? DefaultSalesPriceUsd { get; set; }
        /// <summary>获取或设置默认汇率。</summary>
        public decimal? DefaultExchangeRate { get; set; }
        /// <summary>获取或设置利润率。</summary>
        public decimal? ProfitMargin { get; set; }
        /// <summary>获取或设置默认人民币采购价。</summary>
        public decimal? DefaultPurchasePriceCny { get; set; }
        /// <summary>获取或设置图片路径。</summary>
        public string ImagePath { get; set; }
    }
}
