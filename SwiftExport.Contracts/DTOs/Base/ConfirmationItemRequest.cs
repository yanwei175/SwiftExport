using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.DTOs.Base
{
    public class ConfirmationItemRequest
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
    }
    public class BatchConfirmationRequest
    {
        public List<ConfirmationItemRequest> Items { get; set; } = new List<ConfirmationItemRequest>();
    }
}
