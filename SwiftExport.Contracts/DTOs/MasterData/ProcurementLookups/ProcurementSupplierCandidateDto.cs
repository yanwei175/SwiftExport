using System;

namespace SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups
{
    /// <summary>独立采购供应商新增候选；不包含联系方式、价格或资料编辑字段，不代表最终业务准入。</summary>
    public sealed class ProcurementSupplierCandidateDto
    {
        /// <summary>稳定供应商身份。</summary>
        public Guid Uid { get; set; }

        /// <summary>当前供应商编码，仅供显示和检索。</summary>
        public string SupplierCode { get; set; } = string.Empty;

        /// <summary>当前供应商名称。</summary>
        public string SupplierName { get; set; } = string.Empty;
    }
}
