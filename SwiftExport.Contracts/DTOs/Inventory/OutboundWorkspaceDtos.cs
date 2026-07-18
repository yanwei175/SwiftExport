using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Inventory
{
    public class OutboundSearchRequest : PagedRequestBase
    {
        public string DocumentNo { get; set; }
        public OutboundDocumentTypeEnum? DocumentType { get; set; }
        public OutboundPostingStatusEnum? PostingStatus { get; set; }
        public DateTime? ShipmentDateFrom { get; set; }
        public DateTime? ShipmentDateTo { get; set; }
        public string PackingListNo { get; set; }
        public string SalesOrderNo { get; set; }
        public string Sku { get; set; }
        public string WarehouseKeyword { get; set; }
    }

    public class OutboundListItemDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public string DocumentNo { get; set; }
        public OutboundDocumentTypeEnum DocumentType { get; set; }
        public OutboundPostingStatusEnum PostingStatus { get; set; }
        public DateTime ShipmentDate { get; set; }
        public string WarehouseName { get; set; }
        public string PackingListNo { get; set; }
        public int LineCount { get; set; }
        public decimal TotalQuantity { get; set; }
        public bool IsLegacyReadOnly { get; set; }
        public string ReadOnlyReason { get; set; }
    }

    public class OutboundDetailDto
    {
        public OutboundHeaderDto Header { get; set; }
        public List<OutboundLineDto> Lines { get; set; } = new List<OutboundLineDto>();
        public List<InventoryLedgerItemDto> Ledgers { get; set; } = new List<InventoryLedgerItemDto>();
    }

    public class OutboundHeaderDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public string DocumentNo { get; set; }
        public OutboundDocumentTypeEnum DocumentType { get; set; }
        public OutboundPostingStatusEnum PostingStatus { get; set; }
        public Guid WarehouseSiteId { get; set; }
        public string WarehouseCode { get; set; }
        public string WarehouseName { get; set; }
        public DateTime ShipmentDate { get; set; }
        public Guid? SourcePackingListId { get; set; }
        public string PackingListNo { get; set; }
        public DateTime? PostedDate { get; set; }
        public string PostedBy { get; set; }
        public DateTime? ReversedDate { get; set; }
        public string ReversedBy { get; set; }
        public string ReverseReason { get; set; }
        public bool IsLegacyReadOnly { get; set; }
        public string ReadOnlyReason { get; set; }
    }

    public class OutboundLineDto
    {
        public Guid Uid { get; set; }
        public int LineNumber { get; set; }
        public Guid ProductId { get; set; }
        public string Sku { get; set; }
        public string Description { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }
        public Guid PackingListContributionId { get; set; }
        public Guid NetDemandAllocationId { get; set; }
        public string SalesOrderNo { get; set; }
        public Guid CommandItemId { get; set; }
    }

    public class OutboundTraceDto
    {
        public OutboundDetailDto Document { get; set; }
        public List<string> TraceItems { get; set; } = new List<string>();
    }
}
