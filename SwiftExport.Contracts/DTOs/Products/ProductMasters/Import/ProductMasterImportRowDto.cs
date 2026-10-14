namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.Import
{
    /// <summary>父级型号平表的一行原始输入；空白字段清空对应资料，不传实体身份或版本。</summary>
    public class ProductMasterImportRowDto
    {
        /// <summary>原文件一基行号，批次回执通过 RowIndex 返回此值。</summary>
        public int SourceRowNumber { get; set; }
        public string MasterSku { get; set; }
        public string DefaultDescriptionZh { get; set; }
        public string DefaultDescriptionEn { get; set; }
        public string DefaultFeaturesZh { get; set; }
        public string DefaultFeaturesEn { get; set; }
        public string DefaultPcsPerPackage { get; set; }
        public string DefaultNetWeight { get; set; }
        public string DefaultGrossWeight { get; set; }
        public string DefaultCbm { get; set; }
        /// <summary>产品系列的字典英文名称，保存时匹配有效系列 Uid；空白清空系列。</summary>
        public string ProductSeriesNameEn { get; set; }
        public string DefaultSupplierCode { get; set; }
    }
}
