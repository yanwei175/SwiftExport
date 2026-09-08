using SwiftExport.Contracts.DTOs.Base;
using System;

namespace SwiftExport.Contracts.DTOs.Suppliers
{
    /// <summary>
    /// 供应商产品传输模型。SupplierId/SkuId 是权威关系，编码字段用于人工查阅和冗余追溯。
    /// </summary>
    /// <remarks>来源表：SupplierProduct、Supplier、Product、ProductMaster、ProductCategory。</remarks>
    public class SupplierProductDto : BaseDto
    {
        public Guid SupplierId { get; set; }
        /// <remarks>来源：Supplier.SupplierCode；关联 SupplierProduct.SupplierId = Supplier.Uid。</remarks>
        public string SupplierCode { get; set; } = string.Empty;
        /// <remarks>来源：Supplier.SupplierName；关联 SupplierProduct.SupplierId = Supplier.Uid。</remarks>
        public string SupplierName { get; set; } = string.Empty;

        public Guid SkuId { get; set; }
        /// <remarks>来源：Product.Sku；关联 SupplierProduct.SkuId = Product.Uid。</remarks>
        public string ProductSku { get; set; } = string.Empty;
        /// <remarks>来源：Product.LinkSku。</remarks>
        public string LinkSku { get; set; } = string.Empty;
        /// <remarks>来源：Product.DescriptionEn。</remarks>
        public string DescriptionEn { get; set; } = string.Empty;
        /// <remarks>来源：Product.DescriptionZh。</remarks>
        public string DescriptionZh { get; set; } = string.Empty;
        /// <remarks>来源：Product.FeaturesZh。</remarks>
        public string FeaturesZh { get; set; } = string.Empty;
        /// <remarks>来源：ProductCategory.CategoryName；经 Product.ProductMasterId 和 ProductMaster.CategoryId 关联。</remarks>
        public string CategoryName { get; set; } = string.Empty;
        /// <remarks>来源：Product.MaterialType。</remarks>
        public string MaterialType { get; set; } = string.Empty;
        /// <remarks>来源：Product.MainImage。</remarks>
        public string MainImage { get; set; } = string.Empty;

        public string SupplierSku { get; set; } = string.Empty;
        public string SupplierSkuDescriptionZh { get; set; } = string.Empty;
        public string SupplierSkuFeaturesZh { get; set; } = string.Empty;
        public decimal? UnitPrice { get; set; }
        public decimal? TaxPrice { get; set; }

        /// <remarks>来源：Supplier.Currency。</remarks>
        public string Currency { get; set; } = string.Empty;
        /// <remarks>来源：Supplier.TaxRate。</remarks>
        public decimal? TaxRate { get; set; }
        /// <remarks>计算来源：Product.DefaultSupplierId 与 SupplierProduct.SupplierId 是否相等。</remarks>
        public bool IsProductDefaultSupplier { get; set; }
    }
}
