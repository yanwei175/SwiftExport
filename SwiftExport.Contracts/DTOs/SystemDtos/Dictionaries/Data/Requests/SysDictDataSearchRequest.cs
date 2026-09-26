using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.SystemDtos.Requests
{
    public class SysDictDataSearchRequest : PagedRequestBase
    {
        public List<string> BatchKeywords { get; set; } = new List<string>();
    }
}
