using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    public class PackingListSearchRequest : PagedRequestBase
    {
        public string PackingListNo { get; set; }
        public string InvoiceNo { get; set; }
        public string CustomerKeyword { get; set; }
        public string SalesOrderNo { get; set; }
        public string SupplierKeyword { get; set; }
        public string Sku { get; set; }
        public PackingListPostingStatusEnum? PostingStatus { get; set; }
        public DateTime? ShipmentDateFrom { get; set; }
        public DateTime? ShipmentDateTo { get; set; }
    }

    /// <remarks>来源表：PackingListDocument、Customer、PackingListContributionLine。</remarks>
    public class PackingListListItemDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public string PackingListNo { get; set; }
        public Guid CustomerId { get; set; }
        public string CustomerCode { get; set; }
        public string CustomerName { get; set; }
        public string InvoiceNo { get; set; }
        public DateTime ShipmentDate { get; set; }
        public PackingListLifecycleStatusEnum LifecycleStatus { get; set; }
        public PackingListPostingStatusEnum PostingStatus { get; set; }
        public int LineCount { get; set; }
        public decimal TotalQuantity { get; set; }
        public bool IsLegacyReadOnly { get; set; }
        public string ReadOnlyReason { get; set; }
    }

    public class PackingListDetailDto
    {
        public PackingListHeaderDto Header { get; set; }
        public List<PackingListContributionDto> Lines { get; set; } = new List<PackingListContributionDto>();
    }

    /// <remarks>来源表：PackingListDocument、Customer。</remarks>
    public class PackingListHeaderDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public string PackingListNo { get; set; }
        public Guid CustomerId { get; set; }
        public string CustomerCode { get; set; }
        public string CustomerName { get; set; }
        public string InvoiceNo { get; set; }
        public string ContainerNo { get; set; }
        public string SealNo { get; set; }
        public string ShippingMark { get; set; }
        public string DeliveryAddressSnapshot { get; set; }
        public DateTime PackingDate { get; set; }
        public DateTime ShipmentDate { get; set; }
        public DateTime? Etd { get; set; }
        public DateTime? Eta { get; set; }
        public PackingListLifecycleStatusEnum LifecycleStatus { get; set; }
        public PackingListPostingStatusEnum PostingStatus { get; set; }
        public DateTime? PostedDate { get; set; }
        public string PostedBy { get; set; }
        public DateTime? ReversedDate { get; set; }
        public string ReversedBy { get; set; }
        public string ReverseReason { get; set; }
        public bool IsLegacyReadOnly { get; set; }
        public string ReadOnlyReason { get; set; }
    }

    /// <remarks>来源表：PackingListContributionLine、Product、GrossDemand、NetDemand、NetDemandAllocation、WarehouseSite。</remarks>
    public class PackingListContributionDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public int LineNumber { get; set; }
        public Guid ProductId { get; set; }
        public string Sku { get; set; }
        public string CustomerSku { get; set; }
        public string Description { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }
        public decimal? PackageCount { get; set; }
        public decimal? NetWeight { get; set; }
        public decimal? GrossWeight { get; set; }
        public decimal? Volume { get; set; }
        public string Marks { get; set; }
        public string Note { get; set; }
        public Guid GrossDemandId { get; set; }
        public Guid? NetDemandId { get; set; }
        public Guid? NetDemandAllocationId { get; set; }
        public int DemandVersion { get; set; }
        public int? SourceVersion { get; set; }
        public decimal ShippableRemainingQty { get; set; }
        public Guid? DemandExecutionLinkId { get; set; }
        public Guid? SalesOrderId { get; set; }
        public Guid? SalesOrderLineId { get; set; }
        public string SalesOrderNo { get; set; }
        public int? SalesOrderLineNumber { get; set; }
        public Guid? SourceSupplierId { get; set; }
        public string SourceSupplierName { get; set; }
        public Guid WarehouseSiteId { get; set; }
        public string WarehouseCode { get; set; }
        public string WarehouseName { get; set; }
        public DateTime? RequiredDate { get; set; }
        public byte SupplyType { get; set; }
        public Guid CommandItemId { get; set; }
    }

    public class PackingListSourceSearchRequest : PagedRequestBase
    {
        public Guid? CustomerId { get; set; }
        public string SalesOrderNo { get; set; }
        public string Sku { get; set; }
        public string SupplierKeyword { get; set; }
    }

    /// <remarks>来源表：NetDemandAllocation、NetDemand、GrossDemand、Product、InventoryBalance、WarehouseSite。</remarks>
    public class PackingListSourceDto : PackingListContributionDto
    {
        public string SourceType { get; set; }
        public Guid SourceUid { get; set; }
        public Guid SourceLineId { get; set; }
        public Guid CustomerId { get; set; }
        public string CustomerCode { get; set; }
        public string CustomerName { get; set; }
        public decimal PlannedQty { get; set; }
        public decimal ShippedNetQty { get; set; }
        public decimal SourceInboundNetQty { get; set; }
        public decimal ReadyQty { get; set; }
    }

    public class CreatePackingListDraftCommand
    {
        public Guid CommandId { get; set; }
        public PackingListHeaderCommand Header { get; set; }
        public List<PackingListContributionCommand> Lines { get; set; } = new List<PackingListContributionCommand>();
    }

    public class UpdatePackingListDraftCommand : CreatePackingListDraftCommand
    {
        public int ExpectedVersion { get; set; }
    }

    public class PackingListHeaderCommand
    {
        public string PackingListNo { get; set; }
        public Guid CustomerId { get; set; }
        public DateTime PackingDate { get; set; }
        public DateTime ShipmentDate { get; set; }
        public DateTime? Etd { get; set; }
        public DateTime? Eta { get; set; }
        public string InvoiceNo { get; set; }
        public string ContainerNo { get; set; }
        public string SealNo { get; set; }
        public string ShippingMark { get; set; }
        public string DeliveryAddressSnapshot { get; set; }
    }

    public class PackingListContributionCommand
    {
        public Guid? Uid { get; set; }
        public int? ExpectedVersion { get; set; }
        public Guid GrossDemandId { get; set; }
        public Guid? WarehouseSiteId { get; set; }
        public int ExpectedDemandVersion { get; set; }
        public int? ExpectedSourceVersion { get; set; }
        public decimal ExpectedRemainingQty { get; set; }
        public decimal Quantity { get; set; }
        public decimal? PackageCount { get; set; }
        public decimal? NetWeight { get; set; }
        public decimal? GrossWeight { get; set; }
        public decimal? Volume { get; set; }
        public string Marks { get; set; }
        public string Note { get; set; }
    }

    public class PackingListPreviewCommand
    {
        public int ExpectedVersion { get; set; }
        public string PreviewToken { get; set; }
        public List<string> ConfirmedWarningCodes { get; set; } = new List<string>();
    }

    public class PackingListPostCommand : PackingListPreviewCommand
    {
        public Guid CommandId { get; set; }
    }

    public class PackingListReverseCommand : PackingListPostCommand
    {
        public string Reason { get; set; }
    }

    public class PackingListVoidCommand
    {
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
        public string Reason { get; set; }
    }

    public class PackingListPreviewDto
    {
        public Guid PackingListId { get; set; }
        public int Version { get; set; }
        public bool IsReverse { get; set; }
        public bool HasBlockers { get; set; }
        public string PreviewToken { get; set; }
        public List<PackingListMessageDto> Blockers { get; set; } = new List<PackingListMessageDto>();
        public List<PackingListMessageDto> Warnings { get; set; } = new List<PackingListMessageDto>();
        public List<PackingListPreviewLineDto> Lines { get; set; } = new List<PackingListPreviewLineDto>();
        public List<PackingListInventoryEffectDto> InventoryEffects { get; set; } = new List<PackingListInventoryEffectDto>();
    }

    public class PackingListMessageDto
    {
        public string Code { get; set; }
        public string Message { get; set; }
        public bool RequiresConfirmation { get; set; }
    }

    public class PackingListPreviewLineDto
    {
        public Guid ContributionId { get; set; }
        public Guid? NetDemandAllocationId { get; set; }
        public string Sku { get; set; }
        public decimal PackingQty { get; set; }
        public decimal ShippableRemainingQty { get; set; }
        public decimal SourceAvailableInboundQty { get; set; }
        public decimal AutoInboundQty { get; set; }
        public decimal FinalOutboundQty { get; set; }
        public decimal CustomerSuppliedConsumptionQty { get; set; }
    }

    public class PackingListInventoryEffectDto
    {
        public Guid WarehouseSiteId { get; set; }
        public string WarehouseName { get; set; }
        public Guid ProductId { get; set; }
        public string Sku { get; set; }
        public decimal BalanceBefore { get; set; }
        public decimal AutoInboundQty { get; set; }
        public decimal OutboundQty { get; set; }
        public decimal ConsumptionQty { get; set; }
        public decimal BalanceAfter { get; set; }
    }

    public class PackingListCommandResultDto
    {
        public PackingListCommandStatusEnum Status { get; set; }
        public string ErrorCode { get; set; }
        public string Message { get; set; }
        public Guid PackingListId { get; set; }
        public int Version { get; set; }
        public bool AlreadyApplied { get; set; }
        public List<PackingListGeneratedDocumentDto> GeneratedDocuments { get; set; } = new List<PackingListGeneratedDocumentDto>();
        public List<PackingListContributionResultDto> Contributions { get; set; } = new List<PackingListContributionResultDto>();
    }

    public class PackingListGeneratedDocumentDto
    {
        public string DocumentKind { get; set; }
        public Guid Uid { get; set; }
        public string DocumentNo { get; set; }
    }

    public class PackingListContributionResultDto
    {
        public Guid ContributionId { get; set; }
        public bool Success { get; set; }
        public string ErrorCode { get; set; }
        public string Message { get; set; }
    }

    /// <remarks>来源表：PackingListDocument、PackingListContributionLine、InboundDocument、OutboundDocument、InventoryLedgerEntry。</remarks>
    public class PackingListTraceDto
    {
        public PackingListDetailDto Document { get; set; }
        public List<PackingListTraceItemDto> Items { get; set; } = new List<PackingListTraceItemDto>();
    }

    /// <remarks>来源表：PackingListContributionLine、InboundDocumentLine、OutboundDocumentLine、InventoryLedgerEntry。</remarks>
    public class PackingListTraceItemDto
    {
        public string Stage { get; set; }
        public Guid? Uid { get; set; }
        public string No { get; set; }
        public string Description { get; set; }
        public decimal? Quantity { get; set; }
    }

    /// <remarks>来源表：PackingListDocument、Customer、PackingListContributionLine。</remarks>
    public class PackingListExportDto
    {
        public PackingListHeaderDto Header { get; set; }
        public List<PackingListProductSummaryDto> ProductSummary { get; set; } = new List<PackingListProductSummaryDto>();
        public List<PackingListContributionDto> SourceTrace { get; set; } = new List<PackingListContributionDto>();
    }

    /// <remarks>来源表：PackingListContributionLine、Product。</remarks>
    public class PackingListProductSummaryDto
    {
        public Guid ProductId { get; set; }
        public string Sku { get; set; }
        public string CustomerSku { get; set; }
        public string Description { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }
        public decimal PackageCount { get; set; }
        public decimal NetWeight { get; set; }
        public decimal GrossWeight { get; set; }
        public decimal Volume { get; set; }
    }
}
