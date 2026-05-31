using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.DTOs.Independent
{
    public class IndependentBatchSaveRequest
    {
        List<IndependentDemandDto> CreateList { get; set; }
        List<IndependentDemandDto> UpdateList { get; set; }
        
    }
}
