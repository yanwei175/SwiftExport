using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Workspace
{
    public class SaveSalesOrderRequest
    {
        public SalesOrderEditHeaderDto Header { get; set; }
        public List<SalesOrderEditLineDto> Lines { get; set; } = new List<SalesOrderEditLineDto>();
        public List<SalesOrderDeletedLineDto> DeletedLines { get; set; } = new List<SalesOrderDeletedLineDto>();
    }
}
