using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Orders
{
    /// <summary>
    /// 采购订单详情 DTO（头 + 行）。
    /// </summary>
    public class PurchaseOrderDetailDto
    {
        public PurchaseOrderDto Header { get; set; } = new PurchaseOrderDto();
        public List<PurchaseOrderLineDto> Lines { get; set; } = new List<PurchaseOrderLineDto>();
    }
}
