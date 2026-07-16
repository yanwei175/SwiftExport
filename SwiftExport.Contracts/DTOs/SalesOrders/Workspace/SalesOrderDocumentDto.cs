using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Workspace
{
    public class SalesOrderDocumentDto
    {
        public SalesOrderEditHeaderDto Header { get; set; }
        public List<SalesOrderDocumentLineDto> Lines { get; set; } = new List<SalesOrderDocumentLineDto>();
    }
}
