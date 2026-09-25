using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Detail
{
    /// <summary>关闭销售订单所需的身份、版本和业务原因。</summary>
    public class CloseSalesOrderRequest
    {
        /// <summary>关闭意图的稳定标识。</summary>
        public Guid CommandId { get; set; }
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public string Reason { get; set; }
    }
}
