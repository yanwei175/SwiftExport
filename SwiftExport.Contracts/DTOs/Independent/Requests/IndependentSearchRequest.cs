using SwiftExport.Contracts.Wrappers;
using System;

namespace SwiftExport.Contracts.DTOs.Independent.Requests
{
    public class IndependentSearchRequest : PagedRequestBase
    {
        public string SourceNo { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IsDecomposed { get; set; } = false;

    }
}
