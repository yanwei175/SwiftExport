using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Detail
{
    /// <summary>销售订单详情一次加载所需的订单头、明细和关闭信息。</summary>
    public class SalesOrderDetailDto
    {
        public SalesOrderEditHeaderDto Header { get; set; }
        public List<SalesOrderDetailLineDto> Lines { get; set; } = new List<SalesOrderDetailLineDto>();
        public string CloseReason { get; set; }
        public DateTime? ClosedDate { get; set; }
        public string ClosedBy { get; set; }
    }
}
