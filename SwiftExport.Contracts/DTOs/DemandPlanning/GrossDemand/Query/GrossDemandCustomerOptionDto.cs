using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand
{
    /// <summary>成品需求客户筛选候选；仅提供身份及显示文字，不暴露客户管理资料。</summary>
    public sealed class GrossDemandCustomerOptionDto
    {
        /// <summary>客户身份，用于需求 CustomerIds 精确筛选。</summary>
        public Guid Id { get; set; }

        /// <summary>客户当前编码，仅供筛选显示，不改写需求快照。</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>客户当前名称，仅供筛选显示。</summary>
        public string Name { get; set; } = string.Empty;
    }
}
