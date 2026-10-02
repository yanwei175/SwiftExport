using System;
using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>经服务端验证的本次分配及数量变化，不包含库存预留。</summary>
    public sealed class ProcurementDemandAssociationPreviewDto
    {
        public Guid PurchaseOrderId { get; set; }
        public int Version { get; set; }
        public string Token { get; set; }
        public List<ProcurementDemandAssociationPreviewItemDto> Items { get; set; } = new List<ProcurementDemandAssociationPreviewItemDto>();
    }
}
