using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Detail
{
    /// <summary>
    /// 取消销售订单确认命令。
    /// </summary>
    public class CancelSalesOrderConfirmationCommand
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public Guid CommandId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
