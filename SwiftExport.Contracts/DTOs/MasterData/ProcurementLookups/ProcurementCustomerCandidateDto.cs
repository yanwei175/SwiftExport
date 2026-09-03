using System;

namespace SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups
{
    /// <summary>独立采购客户新增候选；不包含联系方式、价格或资料编辑字段，不代表最终业务准入。</summary>
    public sealed class ProcurementCustomerCandidateDto
    {
        /// <summary>稳定客户身份。</summary>
        public Guid Uid { get; set; }

        /// <summary>当前客户编码，仅供显示和检索。</summary>
        public string CustomerCode { get; set; } = string.Empty;

        /// <summary>当前客户名称。</summary>
        public string CustomerName { get; set; } = string.Empty;
    }
}
