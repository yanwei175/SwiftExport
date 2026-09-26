using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Detail
{
    /// <summary>销售订单普通版本命令的最小请求。</summary>
    public class SalesOrderCommandRequest
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
    }
}
