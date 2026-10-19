using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Contracts.DTOs.OrderTracking.Deliveries
{
    /// <summary>装箱正式交付及反向证据。</summary>
    public class DemandTrackingDeliveryDto
    {
        /// <summary>箱单身份。</summary>
        public Guid PackingListId { get; set; }
        /// <summary>箱单行身份。</summary>
        public Guid PackingListLineId { get; set; }
        /// <summary>箱单号。</summary>
        public string PackingListNo { get; set; }
        /// <summary>箱单行号。</summary>
        public int LineNumber { get; set; }
        /// <summary>该条交付记录数量。</summary>
        public decimal Qty { get; set; }
        /// <summary>交付单位快照。</summary>
        public string Unit { get; set; }
        /// <summary>证据可信且当前已过账未反向时计入正式交付。</summary>
        public bool CountsTowardDelivery { get; set; }
        /// <summary>正式过账时间。</summary>
        public DateTime? PostedDate { get; set; }
        /// <summary>反向时间。</summary>
        public DateTime? ReversedDate { get; set; }
        /// <summary>记录证据需核实原因。</summary>
        public string ReviewReason { get; set; }
    }
}
