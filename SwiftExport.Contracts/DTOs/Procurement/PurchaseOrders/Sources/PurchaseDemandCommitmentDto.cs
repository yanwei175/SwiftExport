using System;
namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>物料转采购时锁定的有效承接数量；不混入入库、跟进或库存进度。</summary>
    public sealed class PurchaseDemandCommitmentDto
    {
        public Guid Uid { get; set; }
        public Guid NetDemandId { get; set; }
        /// <summary>锁定关联的采购头身份，供校验客供件父件实际执行供应商。</summary>
        public Guid TargetHeadId { get; set; }
        /// <summary>锁定关联的采购行身份，不接受客户端伪造。</summary>
        public Guid TargetLineId { get; set; }
        public decimal AllocatedQty { get; set; }
        /// <summary>锁后目标或数量异常的原因；非空时必须阻断，不得当成零占用。</summary>
        public string ReviewReason { get; set; }
    }
}
