using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Inventory
{
    public class InboundSearchRequest : PagedRequestBase
    {
        public string DocumentNo { get; set; }
        public InboundDocumentTypeEnum? DocumentType { get; set; }
        public InboundPostingStatusEnum? PostingStatus { get; set; }
        public DateTime? ReceiptDateFrom { get; set; }
        public DateTime? ReceiptDateTo { get; set; }
        public string SupplierKeyword { get; set; }
        public string WarehouseKeyword { get; set; }
        public string PurchaseOrderNo { get; set; }
        public string SalesOrderNo { get; set; }
        public string Sku { get; set; }
    }

    public class InboundListItemDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public string DocumentNo { get; set; }
        public InboundDocumentTypeEnum DocumentType { get; set; }
        public InboundPostingStatusEnum PostingStatus { get; set; }
        public string PostingStatusText { get; set; }
        public DateTime ReceiptDate { get; set; }
        public string SupplierName { get; set; }
        public string WarehouseName { get; set; }
        public int LineCount { get; set; }
        public decimal TotalQuantity { get; set; }
        public string SourceSummary { get; set; }
        public bool IsLegacyReadOnly { get; set; }
        public string ReadOnlyReason { get; set; }
    }

    public class InboundDetailDto
    {
        public InboundHeaderDto Header { get; set; }
        public List<InboundLineDto> Lines { get; set; } = new List<InboundLineDto>();
        public List<InventoryLedgerItemDto> Ledgers { get; set; } = new List<InventoryLedgerItemDto>();
    }

    public class InboundHeaderDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public string DocumentNo { get; set; }
        public InboundDocumentTypeEnum DocumentType { get; set; }
        public InboundPostingStatusEnum PostingStatus { get; set; }
        public Guid WarehouseSiteId { get; set; }
        public string WarehouseCode { get; set; }
        public string WarehouseName { get; set; }
        public Guid? SupplierId { get; set; }
        public string SupplierCode { get; set; }
        public string SupplierName { get; set; }
        public DateTime ReceiptDate { get; set; }
        public string Remark { get; set; }
        public DateTime? PostedDate { get; set; }
        public string PostedBy { get; set; }
        public DateTime? ReversedDate { get; set; }
        public string ReversedBy { get; set; }
        public string ReverseReason { get; set; }
        public Guid? SourcePackingListId { get; set; }
        public bool IsLegacyReadOnly { get; set; }
        public string ReadOnlyReason { get; set; }
    }

    public class InboundLineDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public int LineNumber { get; set; }
        public Guid ProductId { get; set; }
        public string Sku { get; set; }
        public string Description { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }
        public InboundSourceTypeEnum SourceType { get; set; }
        public Guid SourceUid { get; set; }
        public Guid? DemandExecutionLinkId { get; set; }
        public Guid? NetDemandAllocationId { get; set; }
        public string PurchaseOrderNo { get; set; }
        public string SalesOrderNo { get; set; }
        public decimal RemainingReceivableQty { get; set; }
        public Guid? OriginalLineId { get; set; }
    }

    public class InboundSourceSearchRequest : PagedRequestBase
    {
        public InboundDocumentTypeEnum DocumentType { get; set; }
    }

    public class InboundSourceDto
    {
        public Guid SourceUid { get; set; }
        public int SourceVersion { get; set; }
        public InboundSourceTypeEnum SourceType { get; set; }
        public Guid ProductId { get; set; }
        public string Sku { get; set; }
        public string Description { get; set; }
        public string Unit { get; set; }
        public decimal EffectiveQty { get; set; }
        public decimal ReceivedQty { get; set; }
        public decimal RemainingReceivableQty { get; set; }
        public Guid WarehouseSiteId { get; set; }
        public string WarehouseName { get; set; }
        public Guid? SupplierId { get; set; }
        public string SupplierName { get; set; }
        public Guid? DemandExecutionLinkId { get; set; }
        public Guid? NetDemandAllocationId { get; set; }
        public string PurchaseOrderNo { get; set; }
        public string SalesOrderNo { get; set; }
    }

    public class CreateInboundDraftCommand
    {
        public Guid CommandId { get; set; }
        public InboundDocumentTypeEnum DocumentType { get; set; }
        public DateTime ReceiptDate { get; set; }
        public string Remark { get; set; }
        public List<InboundDraftLineCommand> Lines { get; set; } = new List<InboundDraftLineCommand>();
    }

    public class UpdateInboundDraftCommand
    {
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
        public DateTime ReceiptDate { get; set; }
        public string Remark { get; set; }
        public List<InboundDraftLineCommand> Lines { get; set; } = new List<InboundDraftLineCommand>();
    }

    public class InboundDraftLineCommand
    {
        public Guid? InboundLineId { get; set; }
        public int? ExpectedVersion { get; set; }
        public Guid SourceUid { get; set; }
        public int ExpectedSourceVersion { get; set; }
        public decimal ExpectedRemainingQty { get; set; }
        public decimal Quantity { get; set; }
    }

    public class InboundPostCommand
    {
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
        public string PreviewToken { get; set; }
        public List<InboundPostingLineExpectation> Lines { get; set; } = new List<InboundPostingLineExpectation>();
    }

    public class InboundPostingLineExpectation
    {
        public Guid InboundLineId { get; set; }
        public int ExpectedVersion { get; set; }
        public decimal ExpectedRemainingQty { get; set; }
    }

    public class InboundReverseCommand
    {
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
        public string PreviewToken { get; set; }
        public string Reason { get; set; }
    }

    public class InboundPostPreviewDto
    {
        public Guid InboundDocumentId { get; set; }
        public int Version { get; set; }
        public bool HasBlockers { get; set; }
        public string PreviewToken { get; set; }
        public List<string> Blockers { get; set; } = new List<string>();
        public List<InboundPostPreviewLineDto> Lines { get; set; } = new List<InboundPostPreviewLineDto>();
    }

    public class InboundPostPreviewLineDto
    {
        public Guid InboundLineId { get; set; }
        public decimal Quantity { get; set; }
        public decimal RemainingReceivableQty { get; set; }
        public decimal BalanceBefore { get; set; }
        public decimal BalanceAfter { get; set; }
    }

    public class InboundCommandResultDto
    {
        public Guid InboundDocumentId { get; set; }
        public int Version { get; set; }
        public InboundPostingStatusEnum PostingStatus { get; set; }
        public bool AlreadyApplied { get; set; }
        public string Message { get; set; }
    }

    public class InboundTraceDto
    {
        public InboundDetailDto Document { get; set; }
        public List<string> TraceItems { get; set; } = new List<string>();
    }
}
