using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>新链订单自身身份、商务快照与行；不组合旧列表、跟进或追溯模型。</summary>
    public sealed class ProcurementOrderDetailDto
    {
        public Guid PurchaseOrderId { get; set; }
        public int OriginKind { get; set; }
        public string OrderNo { get; set; }
        public Guid CustomerId { get; set; }
        public string CustomerCode { get; set; }
        public string CustomerName { get; set; }
        public Guid SupplierId { get; set; }
        public string SupplierCode { get; set; }
        public string SupplierName { get; set; }
        public string Currency { get; set; }
        public string BoNo { get; set; }
        public string CustomerPoNo { get; set; }
        public string DemandSourceType { get; set; }
        public Guid? DemandSourceUid { get; set; }
        public string DemandSourceNo { get; set; }
        public Guid ExecutionRequestId { get; set; }
        public string SalesPerson { get; set; }
        public string PurchasePerson { get; set; }
        public string InletHoseType { get; set; }
        public string AeratorType { get; set; }
        public string PalletType { get; set; }
        public string BoxType { get; set; }
        public string LabelType { get; set; }
        public string InstructionManualType { get; set; }
        public string CartonType { get; set; }
        public string LogoType { get; set; }
        public string ReviewedBy { get; set; }
        public string PurchaseOrderFolder { get; set; }
        public string DiscountRemark { get; set; }
        public DateTime OrderDate { get; set; }
        public DateTime? ExpectedDate { get; set; }
        public DateTime? ReplyDate { get; set; }
        public DateTime? ConfirmedDate { get; set; }
        public string ConfirmedBy { get; set; }
        public string Remark { get; set; }
        public string PackagingRequirements { get; set; }
        public int Version { get; set; }
        public ConfirmationStatusEnum ConfirmationStatus { get; set; }
        public PurchaseOrderLifecycleStatusEnum LifecycleStatus { get; set; }
        public Guid? ActiveConfirmationId { get; set; }
        public DateTime? CreatedDate { get; set; }
        public string CreatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }
        public string UpdatedBy { get; set; }
        public List<ProcurementOrderLineDto> Lines { get; set; } = new List<ProcurementOrderLineDto>();
    }
}
