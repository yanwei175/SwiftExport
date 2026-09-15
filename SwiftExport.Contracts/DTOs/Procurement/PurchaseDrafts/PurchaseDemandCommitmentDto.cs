using System;
namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>物料转采购时锁定的有效承接数量；不混入入库、跟进或库存进度。</summary>
    public sealed class PurchaseDemandCommitmentDto
    {
        public Guid Uid { get; set; }
        public Guid NetDemandId { get; set; }
        public decimal AllocatedQty { get; set; }
    }
}
