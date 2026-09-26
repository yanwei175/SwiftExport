using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Inventory
{
    /// <summary>筛选不可变库存流水。</summary>
    public class InventoryLedgerSearchRequest : PagedRequestBase
    {
        public Guid? SupplierId { get; set; }
        public Guid? WarehouseSiteId { get; set; }
        public string SourceDocumentNo { get; set; }
        public InventoryTransactionTypeEnum? TransactionType { get; set; }
        public DateTime? OccurredDateFrom { get; set; }
        public DateTime? OccurredDateTo { get; set; }
    }
}
