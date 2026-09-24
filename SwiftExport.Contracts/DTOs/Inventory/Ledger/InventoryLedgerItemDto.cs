using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Inventory
{
    /// <remarks>来源表：InventoryLedgerEntry、WarehouseSite、Product。</remarks>
    /// <summary>返回一条库存流水和历史来源快照。</summary>
    public class InventoryLedgerItemDto
    {
        public Guid Uid { get; set; }
        public long Sequence { get; set; }
        public Guid WarehouseSiteId { get; set; }
        /// <remarks>来源：WarehouseSite.Code。</remarks>
        public string WarehouseCode { get; set; }
        public string WarehouseName { get; set; }
        public Guid ProductId { get; set; }
        /// <remarks>来源：优先 Product.Sku，为空时使用 InventoryLedgerEntry.SkuSnapshot。</remarks>
        public string Sku { get; set; }
        public string ProductName { get; set; }
        /// <remarks>来源：InventoryLedgerEntry.UnitSnapshot。</remarks>
        public string Unit { get; set; }
        public InventoryTransactionTypeEnum TransactionType { get; set; }
        public decimal QuantityDelta { get; set; }
        public decimal BalanceAfter { get; set; }
        public string SourceDocumentType { get; set; }
        public Guid? SourceDocumentId { get; set; }
        public Guid? SourceLineId { get; set; }
        public string SourceDocumentNo { get; set; }
        public Guid? ReversalOfLedgerId { get; set; }
        public Guid? ReversedByLedgerId { get; set; }
        public Guid? ReplacementOfLedgerId { get; set; }
        public Guid? ReplacedByLedgerId { get; set; }
        public Guid? CorrectionGroupId { get; set; }
        public string Reason { get; set; }
        public DateTime OccurredDate { get; set; }
        public string CreatedBy { get; set; }
    }
}
