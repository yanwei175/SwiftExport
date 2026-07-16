using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Workspace
{
    public class CloseSalesOrderRequest
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public string Reason { get; set; }
    }
}
