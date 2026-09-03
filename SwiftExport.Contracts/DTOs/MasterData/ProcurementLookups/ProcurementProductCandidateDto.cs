using System;

namespace SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups
{
    /// <summary>独立采购产品新增候选；可携带所选供应商关系价格，但不代表最终业务准入。</summary>
    public sealed class ProcurementProductCandidateDto
    {
        /// <summary>稳定产品身份。</summary>
        public Guid Uid { get; set; }

        /// <summary>当前公司产品编码。</summary>
        public string Sku { get; set; } = string.Empty;

        /// <summary>当前中文描述，未配置时为空。</summary>
        public string DescriptionZh { get; set; } = string.Empty;

        /// <summary>当前英文描述，未配置时为空。</summary>
        public string DescriptionEn { get; set; } = string.Empty;

        /// <summary>唯一启用 Unit 字典项的规范基础单位；不是历史单位快照。</summary>
        public string Unit { get; set; } = string.Empty;

        /// <summary>产品当前主图，只用于选择预览。</summary>
        public string MainImage { get; set; } = string.Empty;
        /// <summary>当前客户有效关系中的客户型号；无关系时为空。</summary>
        public string CustomerSku { get; set; } = string.Empty;
        /// <summary>产品当前关联型号。</summary>
        public string LinkSku { get; set; } = string.Empty;
        /// <summary>当前实际供应商有效关系中的供应商型号；无关系时为空。</summary>
        public string SupplierSku { get; set; } = string.Empty;
        /// <summary>产品当前表面处理中文名称。</summary>
        public string FinishZh { get; set; } = string.Empty;
        /// <summary>产品当前中文配置。</summary>
        public string FeaturesZh { get; set; } = string.Empty;
        /// <summary>产品当前物料类型。</summary>
        public string MaterialType { get; set; } = string.Empty;
        /// <summary>产品当前供货方式。</summary>
        public string SupplyType { get; set; } = string.Empty;
        /// <summary>所选实际供应商关系中的未税价；未选择供应商或关系未维护时为空。</summary>
        public decimal? UnitPrice { get; set; }
        /// <summary>所选实际供应商关系中的含税价；未选择供应商或关系未维护时为空。</summary>
        public decimal? TaxPrice { get; set; }
    }
}
