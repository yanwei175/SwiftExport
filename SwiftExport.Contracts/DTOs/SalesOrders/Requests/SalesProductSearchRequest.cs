using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Requests
{
    public class SalesProductSearchRequest : PagedRequestBase
    {
        public List<string> BatchKeywords { get; set; } = new List<string>();
        public Guid CustomerId { get; set; }
        public Guid ColorId { get; set; }
        public string CustomerCode { get; set; }
        public List<string> BatchCustomerSkuList { get; set; }
    }
}
