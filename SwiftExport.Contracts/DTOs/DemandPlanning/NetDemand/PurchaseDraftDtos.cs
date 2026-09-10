using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand
{
    /// <summary>采购草稿预览请求。服务端仍会重新读取活动计划和剩余量。</summary>
    public class PurchaseDraftPreviewRequest
    {
        public Guid ExecutionRequestId { get; set; }
        public PurchaseDraftEntryModeEnum EntryMode { get; set; }
        public PurchaseDraftLineModeEnum LineMode { get; set; }
        public string SourceType { get; set; }
        public Guid? SourceUid { get; set; }
        public List<PurchaseDraftPreviewItem> Items { get; set; } = new List<PurchaseDraftPreviewItem>();
        public List<PurchaseDraftTargetSelectionDto> Targets { get; set; } = new List<PurchaseDraftTargetSelectionDto>();
    }

    /// <summary>客户端选中的物料需求、并发版本及本次采购决策。</summary>
    public class PurchaseDraftPreviewItem
    {
        public Guid CommandItemId { get; set; }
        public Guid NetDemandId { get; set; }
        public int ExpectedVersion { get; set; }
        public decimal ExpectedRemainingQty { get; set; }
        public decimal PurchaseQty { get; set; }
        public Guid SupplierId { get; set; }
        public Guid? DeliveryTargetSupplierId { get; set; }
    }

    /// <summary>来源与供应商对应的采购草稿写入目标。</summary>
    /// <remarks>来源表：NetDemand、Product、Supplier。</remarks>
    public class PurchaseDraftTargetSelectionDto
    {
        public string SourceType { get; set; }
        public Guid SourceUid { get; set; }
        public Guid SupplierId { get; set; }
        public PurchaseDraftTargetModeEnum TargetMode { get; set; }
        public Guid? ExistingPurchaseOrderId { get; set; }
        public int? ExpectedPurchaseOrderVersion { get; set; }
    }

    public class PurchaseDraftPreviewResponse
    {
        public Guid ExecutionRequestId { get; set; }
        public PurchaseDraftEntryModeEnum EntryMode { get; set; }
        public PurchaseDraftLineModeEnum LineMode { get; set; }
        public string Currency { get; set; }
        public string PreviewToken { get; set; }
        public bool HasBlockers { get; set; }
        public bool RequiresConfirmation { get; set; }
        public List<PurchaseDraftPreviewSourceGroupDto> SourceGroups { get; set; } = new List<PurchaseDraftPreviewSourceGroupDto>();
        public List<PurchaseDraftWarningDto> Warnings { get; set; } = new List<PurchaseDraftWarningDto>();
        public List<PurchaseDraftSkippedItemDto> SkippedItems { get; set; } = new List<PurchaseDraftSkippedItemDto>();
    }

    public class PurchaseDraftPreviewSourceGroupDto
    {
        public string SourceType { get; set; }
        public Guid SourceUid { get; set; }
        public string SourceNo { get; set; }
        public string SourcePreviewToken { get; set; }
        public bool CanCreate { get; set; }
        public int DocumentCount { get; set; }
        public List<PurchaseDraftPreviewDocumentDto> Documents { get; set; } = new List<PurchaseDraftPreviewDocumentDto>();
        public List<PurchaseDraftWarningDto> Warnings { get; set; } = new List<PurchaseDraftWarningDto>();
    }

    public class PurchaseDraftPreviewDocumentDto
    {
        public string SourceType { get; set; }
        public Guid SourceUid { get; set; }
        public string SourceNo { get; set; }
        public Guid SupplierId { get; set; }
        public string SupplierCode { get; set; }
        public string SupplierName { get; set; }
        public string Currency { get; set; }
        public PurchaseDraftTargetModeEnum TargetMode { get; set; }
        public Guid? ExistingPurchaseOrderId { get; set; }
        public int? ExpectedPurchaseOrderVersion { get; set; }
        public int SourceLineCount { get; set; }
        public int PreviewLineCount { get; set; }
        public decimal TotalQty { get; set; }
        public DateTime? EarliestPlannedDeliveryDate { get; set; }
        public List<PurchaseDraftPreviewLineDto> Lines { get; set; } = new List<PurchaseDraftPreviewLineDto>();
        public List<SafePurchaseDraftCandidateDto> SafeAppendCandidates { get; set; } = new List<SafePurchaseDraftCandidateDto>();
    }

    public class PurchaseDraftPreviewLineDto
    {
        public Guid? ProductId { get; set; }
        public string Sku { get; set; }
        public string LinkSku { get; set; }
        public string CustomerSku { get; set; }
        public string Description { get; set; }
        public string MaterialType { get; set; }
        public string Unit { get; set; }
        public int QuantityScale { get; set; }
        public string SupplierSku { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? TaxPrice { get; set; }
        public decimal PurchaseQty { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
        public Guid? DeliveryTargetSupplierId { get; set; }
        public string DeliveryTargetSupplierCode { get; set; }
        public string DeliveryTargetSupplierName { get; set; }
        public bool PurchaseDataIncomplete { get; set; }
        public List<DateTime?> SourcePlannedDeliveryDates { get; set; } = new List<DateTime?>();
        public List<PurchaseDraftSourceContributionDto> Contributions { get; set; } = new List<PurchaseDraftSourceContributionDto>();
    }

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

    public class PurchaseDraftWarningDto
    {
        public string Code { get; set; }
        public string Message { get; set; }
        public bool IsBlocker { get; set; }
        public bool RequiresConfirmation { get; set; }
        public string SourceType { get; set; }
        public Guid? SourceUid { get; set; }
        public Guid? SupplierId { get; set; }
        public List<Guid> NetDemandIds { get; set; } = new List<Guid>();
    }

    public class PurchaseDraftSkippedItemDto
    {
        public Guid NetDemandId { get; set; }
        public DemandExecutionTypeEnum ExecutionType { get; set; }
        public string ReasonCode { get; set; }
        public string Message { get; set; }
    }

    /// <remarks>来源表：NetDemand、GrossDemand、Product、Supplier、DemandExecutionLink。</remarks>
    public class SafePurchaseDraftCandidateDto
    {
        public Guid PurchaseOrderId { get; set; }
        public int Version { get; set; }
        public string OrderNo { get; set; }
        public string SourceType { get; set; }
        public Guid SourceUid { get; set; }
        public Guid SupplierId { get; set; }
    }

    /// <summary>预览确认后的采购草稿创建命令；服务端必须重新锁定并校验事实。</summary>
    public class CreatePurchaseDraftsCommand
    {
        public Guid ExecutionRequestId { get; set; }
        public PurchaseDraftEntryModeEnum EntryMode { get; set; }
        public string PreviewToken { get; set; }
        public List<CreatePurchaseDraftSourceCommand> Sources { get; set; } = new List<CreatePurchaseDraftSourceCommand>();
    }

    public class CreatePurchaseDraftSourceCommand
    {
        public string SourceType { get; set; }
        public Guid SourceUid { get; set; }
        public string SourcePreviewToken { get; set; }
        public PurchaseDraftLineModeEnum LineMode { get; set; }
        public List<CreatePurchaseDraftItemCommand> Items { get; set; } = new List<CreatePurchaseDraftItemCommand>();
        public List<CreatePurchaseDraftTargetCommand> Targets { get; set; } = new List<CreatePurchaseDraftTargetCommand>();
        public List<string> ConfirmedWarningCodes { get; set; } = new List<string>();
    }

    public class CreatePurchaseDraftItemCommand
    {
        public Guid CommandItemId { get; set; }
        public Guid NetDemandId { get; set; }
        public int ExpectedVersion { get; set; }
        public decimal ExpectedRemainingQty { get; set; }
        public decimal PurchaseQty { get; set; }
        public Guid SupplierId { get; set; }
        public Guid? DeliveryTargetSupplierId { get; set; }
    }

    public class CreatePurchaseDraftTargetCommand
    {
        public Guid SupplierId { get; set; }
        public PurchaseDraftTargetModeEnum TargetMode { get; set; }
        public Guid? ExistingPurchaseOrderId { get; set; }
        public int? ExpectedPurchaseOrderVersion { get; set; }
    }

    public class CreatePurchaseDraftsResult
    {
        public Guid ExecutionRequestId { get; set; }
        public List<CreatePurchaseDraftSourceResult> Sources { get; set; } = new List<CreatePurchaseDraftSourceResult>();
    }

    public class CreatePurchaseDraftSourceResult
    {
        public string SourceType { get; set; }
        public Guid SourceUid { get; set; }
        public NetDemandCommandOutcomeEnum Outcome { get; set; }
        public string ErrorCode { get; set; }
        public string Message { get; set; }
        public List<CreatedPurchaseDraftDto> PurchaseDrafts { get; set; } = new List<CreatedPurchaseDraftDto>();
    }

    public class CreatedPurchaseDraftDto
    {
        public Guid PurchaseOrderId { get; set; }
        public int Version { get; set; }
        public string OrderNo { get; set; }
        public Guid SupplierId { get; set; }
        public string SupplierCode { get; set; }
        public string SupplierName { get; set; }
        public bool AppendedToExisting { get; set; }
    }

    /// <summary>按毛需求来源展开的采购追溯结果。</summary>
    /// <remarks>来源表：GrossDemand、NetDemand、DemandExecutionLink、PurchaseOrderHead。</remarks>
    public class SourcePurchaseTraceDto
    {
        public string SourceType { get; set; }
        public Guid SourceUid { get; set; }
        public string SourceNo { get; set; }
        public List<SourcePurchaseTraceLineDto> Lines { get; set; } = new List<SourcePurchaseTraceLineDto>();
    }

    /// <remarks>来源表：DemandExecutionLink、PurchaseOrderHead、PurchaseOrderLine。</remarks>
    public class SourcePurchaseTraceLineDto
    {
        public Guid GrossDemandId { get; set; }
        public Guid NetDemandId { get; set; }
        /// <summary>历史计划分配 Uid；新采购 Link 不再写入。</summary>
        public Guid? AllocationId { get; set; }
        public Guid DemandExecutionLinkId { get; set; }
        public DemandExecutionLinkStatusEnum LinkStatus { get; set; }
        public int? SourceLineNumber { get; set; }
        public string Sku { get; set; }
        public decimal NetDemandQty { get; set; }
        public decimal LinkQty { get; set; }
        public decimal OriginalLinkQty { get; set; }
        public decimal ReleasedQty { get; set; }
        public DateTime? DemandRequiredDate { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
        public Guid PurchaseOrderId { get; set; }
        public string PurchaseOrderNo { get; set; }
        public PurchaseOrderLifecycleStatusEnum? PurchaseLifecycleStatus { get; set; }
        public ConfirmationStatusEnum PurchaseConfirmationStatus { get; set; }
        public Guid PurchaseOrderLineId { get; set; }
        public int? PurchaseOrderLineNumber { get; set; }
        public Guid? SupplierId { get; set; }
        public string SupplierCode { get; set; }
        public string SupplierName { get; set; }
    }

    /// <summary>转采购预览和创建使用的稳定警告代码。</summary>
    public static class PurchaseDraftWarningCodes
    {
        public const string InvalidPurchaseQuantity = "INVALID_PURCHASE_QUANTITY";
        public const string PurchaseSupplierRequired = "PURCHASE_SUPPLIER_REQUIRED";
        public const string PurchaseSupplierInvalid = "PURCHASE_SUPPLIER_INVALID";
        public const string DeliveryTargetRequired = "DELIVERY_TARGET_REQUIRED";
        public const string CorruptedPlanningState = "CORRUPTED_PLANNING_STATE";
        public const string PurchaseDataIncomplete = "PURCHASE_DATA_INCOMPLETE";
        public const string DifferentPlannedDeliveryDates = "DIFFERENT_PLANNED_DELIVERY_DATES";
        public const string RelatedCustomerSuppliedRemaining = "RELATED_CUSTOMER_SUPPLIED_REMAINING";
        public const string ExecutionTypeNotPurchase = "EXECUTION_TYPE_NOT_PURCHASE";
    }
}
