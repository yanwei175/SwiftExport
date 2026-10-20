using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto
{
    /// <summary>采购详情页面专用只读快照；只包含采购头、采购行、命令能力与审计信息。</summary>
    public sealed class PurchaseOrderDetailDto
    {
        public Guid PurchaseOrderId { get; set; }
        public int Version { get; set; }
        public string OrderNo { get; set; }
        public string BoNo { get; set; }
        public Guid CustomerId { get; set; }
        public string CustomerCode { get; set; }
        public string CustomerName { get; set; }
        public Guid SupplierId { get; set; }
        public string SupplierCode { get; set; }
        public string SupplierName { get; set; }
        public string Currency { get; set; }
        /// <summary>采购单采用的价格倍率快照；不用于在查询契约中换算或覆盖行价格。</summary>
        public decimal? DiscountRate { get; set; }
        /// <summary>用户手工填写的整单优惠金额快照，不由价格倍率自动计算；空值表示未填写或历史未知。</summary>
        public decimal? DiscountAmount { get; set; }
        public string CustomerPoNo { get; set; }
        public string DemandSourceType { get; set; }
        public Guid? DemandSourceUid { get; set; }
        public string DemandSourceNo { get; set; }
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
        public ConfirmationStatusEnum ConfirmationStatus { get; set; }
        public PurchaseOrderLifecycleStatusEnum LifecycleStatus { get; set; }
        public bool CanEditDraft { get; set; }
        /// <summary>当前是否允许删除整张草稿；服务端执行时仍需持锁复核。</summary>
        public bool CanDeleteDraft { get; set; }
        /// <summary>不能删除草稿的原因，不泄露无权读取的下游信息。</summary>
        public string DeleteDraftBlockReason { get; set; }
        public bool CanConfirm { get; set; }
        public bool CanCancelConfirmation { get; set; }
        public string CommandBlockedReason { get; set; }
        public Guid? ActiveConfirmationId { get; set; }
        public int LineCount { get; set; }
        public decimal TotalQuantity { get; set; }
        public decimal CnyTotalAmount { get; set; }
        public DateTime? CreatedDate { get; set; }
        public string CreatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }
        public string UpdatedBy { get; set; }
        public List<PurchaseOrderLineDetailDto> Lines { get; set; } = new List<PurchaseOrderLineDetailDto>();
    }
}
