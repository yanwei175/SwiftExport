using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Inventory
{
    /// <summary>库存余额与不可变流水净额的只读对账结果，不自动修正任何数据。</summary>
    public class InventoryReconciliationDto
    {
        public decimal BalanceQty { get; set; }
        public decimal LedgerNetQty { get; set; }
        public long BalanceLastLedgerSequence { get; set; }
        public long LedgerLastSequence { get; set; }
        public bool IsReconciled { get; set; }
    }
}
