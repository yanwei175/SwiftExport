using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>采购反向操作的资格、历史影响及本次拟释放贡献，供管理和详情共用确认展示。</summary>
    public sealed class ProcurementReversalPreviewDto
    {
        public Guid PurchaseOrderId { get; set; }
        public int Version { get; set; }
        public string OrderNo { get; set; }
        public ProcurementReversalOperation Operation { get; set; }
        public bool CanExecute { get; set; }
        public List<string> Blockers { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
        public List<ProcurementReversalLineDto> Lines { get; set; } = new List<ProcurementReversalLineDto>();
        public List<ProcurementReleasedContributionDto> Contributions { get; set; } = new List<ProcurementReleasedContributionDto>();
    }
}
