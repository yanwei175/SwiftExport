using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>独立关联用例读取的采购身份、剩余量及现有贡献；不承载采购商务编辑。</summary>
    public sealed class ProcurementDemandAssociationContextDto
    {
        public Guid PurchaseOrderId { get; set; }
        public string OrderNo { get; set; }
        public int Version { get; set; }
        public Guid CustomerId { get; set; }
        public string CustomerCode { get; set; }
        public Guid SupplierId { get; set; }
        public string SupplierName { get; set; }
        public ConfirmationStatusEnum ConfirmationStatus { get; set; }
        public string SourceType { get; set; }
        public Guid? SourceUid { get; set; }
        public string SourceNo { get; set; }
        public List<ProcurementDemandAssociationLineDto> Lines { get; set; } = new List<ProcurementDemandAssociationLineDto>();
    }
}
