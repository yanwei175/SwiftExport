using System;

namespace SwiftExport.Contracts.DTOs.Products.ProductMasters
{
    /// <summary>主型号可写字段；不包含查询展示字段和客户端状态。</summary>
    public class ProductMasterWriteDto
    {
        /// <summary>获取或设置主型号编码。</summary>
        public string MasterSku { get; set; }
        /// <summary>获取或设置默认英文描述。</summary>
        public string DefaultDescriptionEn { get; set; }
        /// <summary>获取或设置默认中文描述。</summary>
        public string DefaultDescriptionZh { get; set; }
        /// <summary>获取或设置默认英文配置。</summary>
        public string DefaultFeaturesEn { get; set; }
        /// <summary>获取或设置默认中文配置。</summary>
        public string DefaultFeaturesZh { get; set; }
        /// <summary>获取或设置默认每箱装率；为空表示未设置，有值时必须为正整数。</summary>
        public int? DefaultPcsPerPackage { get; set; }
        /// <summary>获取或设置默认每箱净重，单位 KG。</summary>
        public decimal? DefaultNetWeight { get; set; }
        /// <summary>获取或设置默认每箱毛重，单位 KG。</summary>
        public decimal? DefaultGrossWeight { get; set; }
        /// <summary>获取或设置默认每箱体积，单位 CBM。</summary>
        public decimal? DefaultCbm { get; set; }
        /// <summary>获取或设置产品类型。</summary>
        public string ProductType { get; set; }
        /// <summary>获取或设置产品标签。</summary>
        public string ProductTag { get; set; }
        /// <summary>获取或设置分类标识。</summary>
        public Guid CategoryId { get; set; }
        /// <summary>获取或设置默认供应商标识。</summary>
        public Guid DefaultSupplierId { get; set; }
    }
}
