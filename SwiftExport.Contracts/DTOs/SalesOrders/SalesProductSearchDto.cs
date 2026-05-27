using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.SalesOrders
{
    public class SalesProductSearchDto : PagedRequestDto
    {
        public int CustomerId { get; set; }
        public int ColorId { get; set; }
        public string CustomerCode { get; set; }
        public List<string> BatchCustomerSkuList { get; set; }
    }
}
