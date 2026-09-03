using System;

namespace SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups
{
    /// <summary>独立采购客户历史筛选项；不包含联系方式、价格或资料编辑字段，不代表最终业务准入。</summary>
    public sealed class ProcurementCustomerFilterDto
    {
        /// <summary>稳定客户身份，用于采购 CustomerId 筛选。</summary>
        public Guid Uid { get; set; }

        /// <summary>当前客户编码，不覆盖旧采购快照。</summary>
        public string CustomerCode { get; set; } = string.Empty;

        /// <summary>当前客户名称，不覆盖旧采购快照。</summary>
        public string CustomerName { get; set; } = string.Empty;

        /// <summary>当前启用状态；停用项仍允许筛选历史。</summary>
        public bool IsActive { get; set; }
    }
}
