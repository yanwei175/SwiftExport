using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Orders.Requests
{
    public class FollowUpSearchRequest : PagedRequestBase
    {
        public List<string> BatchKeywords { get; set; } = new List<string>();
    }
}
