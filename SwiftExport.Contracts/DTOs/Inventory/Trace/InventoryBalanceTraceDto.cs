using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Inventory
{
    /// <remarks>来源表：InventoryBalance、InventoryLedgerEntry、WarehouseSite、Product。</remarks>
    /// <summary>返回余额及其分页流水与对账结果。</summary>
    public class InventoryBalanceTraceDto
    {
        public InventoryBalanceDto Balance { get; set; }
        public List<InventoryLedgerItemDto> Ledgers { get; set; } = new List<InventoryLedgerItemDto>();
        public int TotalCount { get; set; }
        public int PageIndex { get; set; }
        public int PageSize { get; set; }
        public InventoryReconciliationDto Reconciliation { get; set; }
    }
}
