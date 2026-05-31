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
        public int? TempId { get; set; }
        public int? Id { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public string Operation { get; set; }
    }

}
