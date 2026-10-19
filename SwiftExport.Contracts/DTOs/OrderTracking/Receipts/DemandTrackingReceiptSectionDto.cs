using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Contracts.DTOs.OrderTracking.Receipts
{
    /// <summary>收货历史单独核实；失败或未知不影响其他区域。</summary>
    public class DemandTrackingReceiptSectionDto
    {
        /// <summary>查询的成品需求身份。</summary>
        public Guid GrossDemandId { get; set; }
        /// <summary>相关正式收货历史完整性待核实原因。</summary>
        public string ReviewReason { get; set; }
        /// <summary>按入库行身份去重的采购行收货记录，不汇总为需求实收。</summary>
        public List<DemandTrackingReceiptDto> Items { get; set; } = new List<DemandTrackingReceiptDto>();
    }
}
