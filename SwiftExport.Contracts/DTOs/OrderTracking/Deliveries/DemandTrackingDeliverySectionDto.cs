using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Contracts.DTOs.OrderTracking.Deliveries
{
    /// <summary>独立交付区域，异常不遮挡需求基础事实。</summary>
    public class DemandTrackingDeliverySectionDto
    {
        /// <summary>服务端按中国标准时间取得的本次判定业务日。</summary>
        public DateTime BusinessDate { get; set; }
        /// <summary>活动需求交期早于业务日且可信未交大于零；无交期或证据异常为未知。</summary>
        public bool? IsOverdue { get; set; }
        /// <summary>确定逾期的自然日数，确定未逾期为零，未知为空。</summary>
        public int? OverdueDays { get; set; }
        /// <summary>当前需求身份。</summary>
        public Guid GrossDemandId { get; set; }
        /// <summary>可信正式交付净额，证据异常为null。</summary>
        public decimal? DeliveredQty { get; set; }
        /// <summary>需求数量减可信正式交付净额，未知为null。</summary>
        public decimal? RemainingQty { get; set; }
        /// <summary>成品需求交付单位。</summary>
        public string Unit { get; set; }
        /// <summary>交付完整性或来源证据待核实原因。</summary>
        public string ReviewReason { get; set; }
        /// <summary>正式及反向历史记录。</summary>
        public List<DemandTrackingDeliveryDto> Items { get; set; } = new List<DemandTrackingDeliveryDto>();
    }
}
