using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Products.Requests
{
    public class BomSearchRequest : PagedRequestBase
    {
        public List<string> BatchKeywords { get; set; } = new List<string>();
    }
}
