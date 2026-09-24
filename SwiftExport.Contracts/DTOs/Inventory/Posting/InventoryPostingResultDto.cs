using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Inventory
{
    /// <summary>返回库存命令明细对应的流水和余额变化。</summary>
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
