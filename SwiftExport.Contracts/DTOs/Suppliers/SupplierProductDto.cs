using SwiftExport.Contracts.DTOs.Base;
using System;

namespace SwiftExport.Contracts.DTOs.Suppliers
{
    /// <summary>
    /// 供应商产品传输模型。SupplierId/SkuId 是权威关系，编码字段用于人工查阅和冗余追溯。
    /// </summary>
    public class SupplierProductDto : BaseDto
    {
        public Guid SupplierId { get; set; }
        public string SupplierCode { get; set; } = string.Empty;
        public string SupplierName { get; set; } = string.Empty;

        public Guid SkuId { get; set; }
        public string ProductSku { get; set; } = string.Empty;
        public string LinkSku { get; set; } = string.Empty;
        public string DescriptionEn { get; set; } = string.Empty;
        public string DescriptionZh { get; set; } = string.Empty;
        public string FeaturesZh { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string MaterialType { get; set; } = string.Empty;
        public string MainImage { get; set; } = string.Empty;

        public string SupplierSku { get; set; } = string.Empty;
        public string SupplierSkuDescriptionZh { get; set; } = string.Empty;
        public string SupplierSkuFeaturesZh { get; set; } = string.Empty;
        public decimal? UnitPrice { get; set; }
        public decimal? TaxPrice { get; set; }

        public string Currency { get; set; } = string.Empty;
        public decimal? TaxRate { get; set; }
    }
}
