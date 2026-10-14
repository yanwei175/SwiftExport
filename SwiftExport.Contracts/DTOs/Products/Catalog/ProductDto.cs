using SwiftExport.Contracts.DTOs.Base;
using System;


namespace SwiftExport.Contracts.DTOs.Products
{
    /// <summary>产品资料的保存与查询契约；关联 ID 可写，关联名称由当前资料查询投影。</summary>
    public class ProductDto : BaseDto
    {
        // ERP SKU级别管理字段
        /// <summary>独立引用的主型号身份；空值表示清除。</summary>
        public Guid? ProductMasterId { get; set; }
        /// <summary>独立引用的中性产品身份；空值表示清除。</summary>
        public Guid? ProductNeutralId { get; set; }
        /// <summary>引用主型号的当前编码，只读查询投影。</summary>
        public string MasterSku { get; set; }
        public string Sku { get; set; }
        public string DescriptionEn { get; set; } 
        public string DescriptionZh { get; set; }
        public string FeaturesEn { get; set; } 
        public string FeaturesZh { get; set; } 
        public string Finish { get; set; }
        public string MaterialType { get; set; }
        /// <summary>产品当前基础计量单位；业务上允许为空。</summary>
        public string Unit { get; set; }
        public string SupplyType { get; set; }// 供应方式：采购/生产/外协。
        public int BomVersion { get; set; }   // BOM版本：0=无BOM，>0=有BOM。
        public string BomNo { get; set; }
        public string Note { get; set; }
        /// <summary>引用中性产品的当前编码，只读查询投影。</summary>
        public string LinkSku { get; set; }

        // 价格
        public decimal CNYPrice { get; set; }
        public decimal USDPrice { get; set; }


        // 装箱基础数据（供销售订单行带出）
        public decimal? PcsOfCarton { get; set; }
        public decimal? NwOfCarton { get; set; }
        public decimal? GwOfCarton { get; set; }
        public decimal? CbmOfCarton { get; set; }

        // 聚合信息（只读投影；分类经 ProductMaster 联表，保存时后端忽略 CategoryId）
        /// <summary>主型号当前分类标识；空 Guid 表示未分类。</summary>
        public Guid CategoryId { get; set; }
        public Guid ColorId { get; set; }

        /// <summary>主型号当前分类名称；未设置时返回“未分类”。</summary>
        public string CategoryName { get; set; }
        public string SupplierName { get; set; }

        // 采购供应商身份及其他可选参考关系；名称和编码由查询联表投影。
        public Guid? DefaultSupplierId { get; set; }
        /// <summary>建议仓库标识，仅供参考，不决定入出库单头仓库。</summary>
        public Guid? DefaultWarehouseSiteId { get; set; }
        /// <summary>当前建议仓库编码，只读查询投影。</summary>
        public string DefaultWarehouseCode { get; set; }
        /// <summary>当前建议仓库名称，只读查询投影。</summary>
        public string DefaultWarehouseName { get; set; }
        /// <summary>实际制造商标识，独立于默认采购供应商。</summary>
        public Guid? ManufacturerId { get; set; }
        /// <summary>当前制造商编码，只读查询投影。</summary>
        public string ManufacturerCode { get; set; }
        /// <summary>当前制造商名称，只读查询投影。</summary>
        public string ManufacturerName { get; set; }
        /// <summary>制造商型号；可不选择制造商而单独保存。</summary>
        public string ManufacturerModel { get; set; }
        public string DefaultSupplierCode { get; set; }

        // 图片路径 (如果有)
        public string MainImage { get; set; }
    }
}
