using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Workspace
{
    public class SalesOrderCommandRequest
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
    }
}
