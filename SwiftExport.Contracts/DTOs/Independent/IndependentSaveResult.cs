using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.DTOs.Independent
{
    public class IndependentBatchSaveResponse
    {
        public List<IndependentSaveResult> Results { get; set; }
    }

    public class IndependentSaveResult
    {
        public Guid? TempId { get; set; }
        public Guid? Uid { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public string Operation { get; set; }
    }

}
