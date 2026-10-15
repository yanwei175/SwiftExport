using System;

namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses
{
    /// <summary>回填主型号版本及默认中性产品的展示资料，不承载其他主型号编辑字段。</summary>
    public class ProductMasterMainImageResult
    {
        /// <summary>获取或设置主型号身份。</summary>
        public Guid ProductMasterId { get; set; }
        /// <summary>获取或设置保存后的主型号版本。</summary>
        public int NewVersion { get; set; }
        /// <summary>获取或设置被选为默认值的中性产品身份。</summary>
        public Guid ProductNeutralId { get; set; }
        /// <summary>获取或设置默认主图地址。</summary>
        public string MainImagePath { get; set; }
        /// <summary>获取或设置默认中性产品的美元销售价。</summary>
        public decimal? DefaultSalesPriceUsd { get; set; }
        /// <summary>获取或设置默认中性产品的人民币采购价。</summary>
        public decimal? DefaultPurchasePriceCny { get; set; }
    }
}
