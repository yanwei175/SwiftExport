using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
/// <summary>合并采购行仍保留的逐物料需求来源贡献。</summary>
    public class PurchaseDraftSourceContributionDto
    {
        public Guid CommandItemId { get; set; }
        public Guid NetDemandId { get; set; }
        public int NetDemandVersion { get; set; }
        public Guid SupplierId { get; set; }
        public Guid? DeliveryTargetSupplierId { get; set; }
        public Guid SourceLineId { get; set; }
        public int? SourceLineNumber { get; set; }
        public decimal ExpectedRemainingQty { get; set; }
        public decimal PurchaseQty { get; set; }
        public DateTime? DemandRequiredDate { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
    }
}

