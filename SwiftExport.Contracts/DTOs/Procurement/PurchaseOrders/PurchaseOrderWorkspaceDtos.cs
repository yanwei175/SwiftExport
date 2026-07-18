using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders
{
    public class PurchaseOrderSearchRequest : PagedRequestBase
    {
        public string OrderNo { get; set; }
        public string SourceNo { get; set; }
        public Guid? SupplierId { get; set; }
        public string SupplierKeyword { get; set; }
        public string Sku { get; set; }
        public ConfirmationStatusEnum? ConfirmationStatus { get; set; }
        public PurchaseOrderLifecycleStatusEnum? LifecycleStatus { get; set; }
        public SupplierArrangementStatusEnum? ArrangementStatus { get; set; }
        public DateTime? PlannedDeliveryDateFrom { get; set; }
        public DateTime? PlannedDeliveryDateTo { get; set; }
        public DateTime? ExpectedReadyDateFrom { get; set; }
        public DateTime? ExpectedReadyDateTo { get; set; }
    }

    public class PurchaseOrderListItemDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public string OrderNo { get; set; }
        public string BoNo { get; set; }
        public DateTime? OrderDate { get; set; }
        public DateTime? ExpectedDate { get; set; }
        public DateTime? ReplyDate { get; set; }
        public Guid? SupplierId { get; set; }
        public string SupplierCode { get; set; }
        public string SupplierName { get; set; }
        public string DemandSourceType { get; set; }
        public Guid? DemandSourceUid { get; set; }
        public string DemandSourceNo { get; set; }
        public ConfirmationStatusEnum ConfirmationStatus { get; set; }
        public PurchaseOrderLifecycleStatusEnum? LifecycleStatus { get; set; }
        public int LineCount { get; set; }
        public decimal TotalQty { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal FinalShippedQty { get; set; }
        public string OrderStatusText { get; set; }
        public string UnshippedQtyText { get; set; }
        public int ArrangedLineCount { get; set; }
        public int ReportedLineCount { get; set; }
        public decimal LatestReadyQty { get; set; }
        public DateTime? LatestExpectedReadyDate { get; set; }
        public bool IsLegacyReadOnly { get; set; }
        public string ReadOnlyReason { get; set; }
        public bool CanEditDraft { get; set; }
        public bool CanConfirm { get; set; }
        public bool CanCancelConfirmation { get; set; }
        public bool CanReportProgress { get; set; }
    }

    public class PurchaseOrderDetailDto
    {
        public PurchaseOrderListItemDto Head { get; set; }
        public string SalesOrderNo { get; set; }
        public string CustomerCode { get; set; }
        public string CustomerPoNo { get; set; }
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
        public string Remark { get; set; }
        public DateTime? ConfirmedDate { get; set; }
        public string ConfirmedBy { get; set; }
        public DateTime? CreatedDate { get; set; }
        public string CreatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }
        public string UpdatedBy { get; set; }
        public string CurrencyText { get; set; }
        public string UsdTotalText { get; set; }
        public string CartonTotalText { get; set; }
        public string CbmTotalText { get; set; }
        public string InboundStatusText { get; set; }
        public string ShippedStatusText { get; set; }
        public List<PurchaseOrderLineDetailDto> Lines { get; set; } = new List<PurchaseOrderLineDetailDto>();
        public List<BusinessOperationTraceItemDto> Operations { get; set; } = new List<BusinessOperationTraceItemDto>();
    }

    public class PurchaseOrderLineDetailDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public int LineNumber { get; set; }
        public Guid? ProductId { get; set; }
        public string Sku { get; set; }
        public string CustomerSku { get; set; }
        public string LinkSku { get; set; }
        public string SupplierSku { get; set; }
        public string DescriptionZh { get; set; }
        public string FinishZh { get; set; }
        public string FeaturesZh { get; set; }
        public string MaterialType { get; set; }
        public string SupplyType { get; set; }
        public string MainImage { get; set; }
        public decimal Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? TaxPrice { get; set; }
        public decimal? UnitAmount { get; set; }
        public decimal? TaxAmount { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
        public Guid? DeliveryTargetAllocationId { get; set; }
        public Guid? DeliveryTargetSupplierId { get; set; }
        public string DeliverySupplierName { get; set; }
        public string SubNote { get; set; }
        public decimal LinkedQty { get; set; }
        public decimal RecQty { get; set; }
        public string UnreceivedQtyText { get; set; }
        public SupplierProgressReportDto LatestProgress { get; set; }
        public List<PurchaseOrderSourceContributionDto> Contributions { get; set; } = new List<PurchaseOrderSourceContributionDto>();
        public List<SupplierProgressReportDto> ProgressHistory { get; set; } = new List<SupplierProgressReportDto>();
    }

    public class PurchaseOrderSourceContributionDto
    {
        public Guid LinkId { get; set; }
        public Guid PurchaseOrderLineId { get; set; }
        public Guid AllocationId { get; set; }
        public Guid NetDemandId { get; set; }
        public Guid GrossDemandId { get; set; }
        public string SourceType { get; set; }
        public Guid? SourceUid { get; set; }
        public string SourceNo { get; set; }
        public int? SourceLineNumber { get; set; }
        public string Sku { get; set; }
        public decimal AllocatedQty { get; set; }
        public decimal ReleasedQty { get; set; }
        public decimal EffectiveQty { get; set; }
        public DateTime? DemandRequiredDate { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
    }

    public class PurchaseOrderTraceDto
    {
        public Guid PurchaseOrderId { get; set; }
        public string OrderNo { get; set; }
        public List<PurchaseOrderSourceContributionDto> Contributions { get; set; } = new List<PurchaseOrderSourceContributionDto>();
        public List<BusinessOperationTraceItemDto> Operations { get; set; } = new List<BusinessOperationTraceItemDto>();
    }

    public class SupplierProgressReportDto
    {
        public Guid Uid { get; set; }
        public Guid PurchaseOrderLineId { get; set; }
        public int Sequence { get; set; }
        public SupplierArrangementStatusEnum ArrangementStatus { get; set; }
        public decimal ReadyQty { get; set; }
        public DateTime? ExpectedReadyDate { get; set; }
        public DateTime SupplierReportedDate { get; set; }
        public string Note { get; set; }
        public bool IsCorrection { get; set; }
        public string CorrectionReason { get; set; }
        public Guid CommandId { get; set; }
        public DateTime? CreatedDate { get; set; }
        public string CreatedBy { get; set; }
    }

    public class BusinessOperationTraceItemDto
    {
        public Guid Uid { get; set; }
        public string OperationType { get; set; }
        public string OperationName { get; set; }
        public string Reason { get; set; }
        public string Summary { get; set; }
        public string ChangedFieldsJson { get; set; }
        public string RelatedObjectsJson { get; set; }
        public string UserName { get; set; }
        public DateTime OccurredDate { get; set; }
        public string CorrelationId { get; set; }
    }

    public class UpdatePurchaseOrderDraftCommand
    {
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
        public bool UpdateLegacyHeaderFields { get; set; }
        public string Remark { get; set; }
        public string BoNo { get; set; }
        public string CustomerPoNo { get; set; }
        public string SalesPerson { get; set; }
        public string PurchasePerson { get; set; }
        public DateTime? ReplyDate { get; set; }
        public string InletHoseType { get; set; }
        public string AeratorType { get; set; }
        public string PalletType { get; set; }
        public string BoxType { get; set; }
        public string LabelType { get; set; }
        public string InstructionManualType { get; set; }
        public string CartonType { get; set; }
        public string LogoType { get; set; }
        public string DiscountRemark { get; set; }
        public List<UpdatePurchaseOrderDraftLineCommand> Lines { get; set; } = new List<UpdatePurchaseOrderDraftLineCommand>();
    }

    public class UpdatePurchaseOrderDraftLineCommand
    {
        public Guid PurchaseOrderLineId { get; set; }
        public int ExpectedVersion { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
        public string SupplierSku { get; set; }
        public string DescriptionZh { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? TaxPrice { get; set; }
        public string SubNote { get; set; }
    }

    public class ConfirmPurchaseOrderCommand
    {
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
    }

    public class CancelPurchaseOrderConfirmationCommand
    {
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
        public string Reason { get; set; }
    }

    public class SupplierProgressReportCommand
    {
        public Guid CommandId { get; set; }
        public Guid PurchaseOrderLineId { get; set; }
        public int ExpectedPurchaseOrderVersion { get; set; }
        public int ExpectedLineVersion { get; set; }
        public SupplierArrangementStatusEnum ArrangementStatus { get; set; }
        public decimal ReadyQty { get; set; }
        public DateTime? ExpectedReadyDate { get; set; }
        public DateTime SupplierReportedDate { get; set; }
        public string Note { get; set; }
        public string CorrectionReason { get; set; }
    }

    public class ProcurementCommandResult
    {
        public ProcurementCommandOutcomeEnum Outcome { get; set; }
        public Guid PurchaseOrderId { get; set; }
        public int Version { get; set; }
        public ConfirmationStatusEnum ConfirmationStatus { get; set; }
        public Guid? SupplierProgressReportId { get; set; }
        public string Message { get; set; }
    }
}
