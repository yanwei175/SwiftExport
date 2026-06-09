using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.DTOs.Independent
{
    public class IndependSearchRequest : PagedRequestDto
    {
        public string SourceNo { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IsDecomposed { get; set; } = false;
        public string Sku { get; set; }
        public IndependentDemandStatusEnum StatusFlag { get; set; }

    }
}
