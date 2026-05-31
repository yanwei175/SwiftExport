using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.DTOs.Independent
{
    public class IndependentSearchDto : PagedRequestDto
    {
        public string SourceNo { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IsDecomposed { get; set; }=false;

    }
}
