using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Inventory
{
    public class WarehouseSiteSearchRequest : PagedRequestBase
    {
        public Guid? SupplierId { get; set; }
        public bool? IsActive { get; set; }
    }

    public class WarehouseSiteDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public Guid? SupplierId { get; set; }
        public string SupplierCode { get; set; }
        public string SupplierName { get; set; }
        public bool IsActive { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }

    public class CreateSupplierVirtualWarehouseCommand
    {
        public Guid CommandId { get; set; }
        public Guid SupplierId { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
    }

    public class UpdateWarehouseSiteCommand
    {
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
        public string Name { get; set; }
    }

    public class DeactivateWarehouseSiteCommand
    {
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
        public string Reason { get; set; }
    }

    public class InventoryBalanceSearchRequest : PagedRequestBase
    {
        public Guid? SupplierId { get; set; }
        public Guid? WarehouseSiteId { get; set; }
        public bool? NonZeroOnly { get; set; }
    }

    public class InventoryBalanceDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public Guid WarehouseSiteId { get; set; }
        public string WarehouseCode { get; set; }
        public string WarehouseName { get; set; }
        public Guid ProductId { get; set; }
        public string Sku { get; set; }
        public string ProductName { get; set; }
        public string Unit { get; set; }
        public decimal OnHandQty { get; set; }
        public decimal AvailableQty { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }

    public class InventoryLedgerSearchRequest : PagedRequestBase
    {
        public Guid? SupplierId { get; set; }
        public Guid? WarehouseSiteId { get; set; }
        public string SourceDocumentNo { get; set; }
        public InventoryTransactionTypeEnum? TransactionType { get; set; }
        public DateTime? OccurredDateFrom { get; set; }
        public DateTime? OccurredDateTo { get; set; }
    }

    public class InventoryLedgerItemDto
    {
        public Guid Uid { get; set; }
        public long Sequence { get; set; }
        public Guid WarehouseSiteId { get; set; }
        public string WarehouseCode { get; set; }
        public Guid ProductId { get; set; }
        public string Sku { get; set; }
        public string Unit { get; set; }
        public InventoryTransactionTypeEnum TransactionType { get; set; }
        public decimal QuantityDelta { get; set; }
        public decimal BalanceAfter { get; set; }
        public string SourceDocumentType { get; set; }
        public Guid? SourceDocumentId { get; set; }
        public Guid? SourceLineId { get; set; }
        public string SourceDocumentNo { get; set; }
        public Guid? ReversalOfLedgerId { get; set; }
        public string Reason { get; set; }
        public DateTime OccurredDate { get; set; }
        public string CreatedBy { get; set; }
    }

    public class InventoryBalanceTraceDto
    {
        public InventoryBalanceDto Balance { get; set; }
        public List<InventoryLedgerItemDto> Ledgers { get; set; } = new List<InventoryLedgerItemDto>();
    }

    public class OpeningBalanceLineCommand
    {
        public Guid CommandItemId { get; set; }
        public Guid WarehouseSiteId { get; set; }
        public Guid ProductId { get; set; }
        public decimal QuantityDelta { get; set; }
        public string Reason { get; set; }
    }

    public class OpeningBalanceCommand
    {
        public Guid CommandId { get; set; }
        public List<OpeningBalanceLineCommand> Lines { get; set; } = new List<OpeningBalanceLineCommand>();
    }

    public class InventoryPostingResultDto
    {
        public Guid CommandItemId { get; set; }
        public Guid LedgerId { get; set; }
        public decimal BeforeQty { get; set; }
        public decimal Delta { get; set; }
        public decimal AfterQty { get; set; }
        public bool AlreadyApplied { get; set; }
    }
}
