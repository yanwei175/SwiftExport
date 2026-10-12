using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Products.ProductMasters
{
    /// <summary>主型号管理列表项，组合可写数据与实时展示名称。</summary>
    public class ProductMasterListItemDto : BaseDto
    {
        /// <summary>获取或设置主型号可写数据。</summary>
        public ProductMasterWriteDto Data { get; set; } = new ProductMasterWriteDto();
        /// <summary>获取或设置实时分类名称。</summary>
        public string CategoryName { get; set; }
        /// <summary>获取或设置实时默认供应商名称。</summary>
        public string DefaultSupplierName { get; set; }
        /// <summary>获取或设置中性产品标记为主图的只读图片路径。</summary>
        public string MainImagePath { get; set; }
        /// <summary>只读标签摘要，例如“安装方式：吸顶、挂墙；尺寸：8寸”；不用于写入旧 ProductTag。</summary>
        public string TagSummary { get; set; } = string.Empty;
        /// <summary>获取或设置默认主图中性产品的美元销售价。</summary>
        public decimal? DefaultSalesPriceUsd { get; set; }
        /// <summary>获取或设置默认主图中性产品的人民币采购价。</summary>
        public decimal? DefaultPurchasePriceCny { get; set; }
    }
}
