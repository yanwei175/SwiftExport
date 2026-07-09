using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Independent.Responses
{
    public class IndependentBatchSaveResponse : BatchSaveResponseBase
    {
        public List<IndependentSaveResult> Results { get; set; } = new List<IndependentSaveResult>();
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
