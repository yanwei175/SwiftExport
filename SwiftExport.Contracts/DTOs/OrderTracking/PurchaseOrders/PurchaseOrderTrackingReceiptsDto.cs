using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.DTOs.OrderTracking.Receipts;

namespace SwiftExport.Contracts.DTOs.OrderTracking.PurchaseOrders
{
    /// <summary>采购单整行收货证据，按采购行和入库行身份去重，不向需求分摊。</summary>
    public sealed class PurchaseOrderTrackingReceiptsDto
    {
        /// <summary>所查询采购单身份。</summary>
        public Guid PurchaseOrderId { get; set; }
        /// <summary>采购行正式收货及反向历史。</summary>
        public List<DemandTrackingReceiptDto> Items { get; set; } = new List<DemandTrackingReceiptDto>();
        /// <summary>采购整行可信净收货进度，不跨单位汇总。</summary>
        public List<PurchaseLineReceiptProgressDto> PurchaseLines { get; set; } = new List<PurchaseLineReceiptProgressDto>();
        /// <summary>历史证据完整性待核实原因。</summary>
        public string ReviewReason { get; set; }
    }
}
