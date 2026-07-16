using SwiftExport.Contracts.Wrappers;
using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Workspace
{
    public class SalesOrderProductSearchRequest : PagedRequestBase
    {
        public Guid? CustomerId { get; set; }
    }
}
