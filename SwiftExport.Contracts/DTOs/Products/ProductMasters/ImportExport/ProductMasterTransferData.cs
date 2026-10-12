using System;
namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport
{
    /// <summary>预览与平表导出的父级资料值，不携带将退出的类型、标签或子级资料。</summary>
    public class ProductMasterTransferData
    {
        /// <summary>主型号编码。</summary>
        public string MasterSku { get; set; }
        /// <summary>中文描述。</summary>
        public string DefaultDescriptionZh { get; set; }
        /// <summary>英文描述。</summary>
        public string DefaultDescriptionEn { get; set; }
        /// <summary>中文配置。</summary>
        public string DefaultFeaturesZh { get; set; }
        /// <summary>英文配置。</summary>
        public string DefaultFeaturesEn { get; set; }
        /// <summary>每箱装率，新增默认 1。</summary>
        public int DefaultPcsPerPackage { get; set; } = 1;
        /// <summary>每箱净重 KG。</summary>
        public decimal? DefaultNetWeight { get; set; }
        /// <summary>每箱毛重 KG。</summary>
        public decimal? DefaultGrossWeight { get; set; }
        /// <summary>每箱体积 CBM。</summary>
        public decimal? DefaultCbm { get; set; }
        /// <summary>分类身份，未设置为空 Guid。</summary>
        public Guid CategoryId { get; set; }
        /// <summary>供应商身份，未设置为空 Guid。</summary>
        public Guid DefaultSupplierId { get; set; }
    }
}
