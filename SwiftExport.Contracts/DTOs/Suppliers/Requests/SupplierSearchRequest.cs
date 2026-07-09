using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Suppliers.Requests
{
    public class SupplierSearchRequest : PagedRequestBase
    {
        public List<string> BatchKeywords { get; set; } = new List<string>();
    }
}
