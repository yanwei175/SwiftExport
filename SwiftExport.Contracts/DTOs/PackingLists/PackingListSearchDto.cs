using SwiftExport.Contracts.Wrappers;
using System;

namespace SwiftExport.Contracts.DTOs.PackingLists
{
    public class PackingListSearchDto : PagedRequestDto
    {
        public string PackingListNo { get; set; }
        public string SalesOrderNo { get; set; }
        public string InvoiceNo { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; } 
        public bool? IsPosted { get; set; }
    }
}
