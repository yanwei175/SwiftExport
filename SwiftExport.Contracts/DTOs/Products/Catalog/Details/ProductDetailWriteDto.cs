using System;
namespace SwiftExport.Contracts.DTOs.Products
{
    /// <summary>物料详情可写资料及两个独立来源身份；不携带模板、BOM、启停或包装。</summary>
    public class ProductDetailWriteDto
    {
        /// <summary>物料编号。</summary>
        public string Sku { get; set; }
        /// <summary>独立引用的主型号身份；空值表示清除。</summary>
        public Guid? ProductMasterId { get; set; }
        /// <summary>物料自身的产品型号；可自由填写，不要求存在中性产品。</summary>
        public string LinkSku { get; set; }
        /// <summary>英文描述。</summary>
        public string DescriptionEn { get; set; }
        /// <summary>中文描述。</summary>
        public string DescriptionZh { get; set; }
        /// <summary>英文配置。</summary>
        public string FeaturesEn { get; set; }
        /// <summary>中文配置。</summary>
        public string FeaturesZh { get; set; }
        /// <summary>颜色身份；空值表示清除。</summary>
        public Guid? ColorId { get; set; }
        /// <summary>物料类型。</summary>
        public string MaterialType { get; set; }
        /// <summary>基础单位英文代码；允许为空。</summary>
        public string Unit { get; set; }
        /// <summary>供货方式。</summary>
        public string SupplyType { get; set; }
        /// <summary>备注。</summary>
        public string Note { get; set; }
        /// <summary>完整图片地址或本地路径。</summary>
        public string MainImage { get; set; }
        /// <summary>人民币参考价。</summary>
        public decimal? CNYPrice { get; set; }
        /// <summary>美元参考价。</summary>
        public decimal? USDPrice { get; set; }
        /// <summary>单品重量（KG），区别于包装箱重。</summary>
        public decimal? UnitWeight { get; set; }
        /// <summary>单品体积（CBM），区别于包装箱体积。</summary>
        public decimal? UnitCBM { get; set; }
        /// <summary>默认供应商身份。</summary>
        public Guid? DefaultSupplierId { get; set; }
        /// <summary>制造商身份。</summary>
        public Guid? ManufacturerId { get; set; }
        /// <summary>制造商型号；不因清除制造商自动清空。</summary>
        public string ManufacturerModel { get; set; }
        /// <summary>参考仓库身份；不决定单据实际仓库。</summary>
        public Guid? DefaultWarehouseSiteId { get; set; }
    }
}
