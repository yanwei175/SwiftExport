using System;

namespace SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups
{
    /// <summary>独立采购标准采购产品新增候选；不包含联系方式、价格或资料编辑字段，不代表最终业务准入。</summary>
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
    }
}
