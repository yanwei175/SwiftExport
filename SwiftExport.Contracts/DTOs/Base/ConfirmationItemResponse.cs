using SwiftExport.Contracts.SysEnums.StaticEnums;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.DTOs.Base
{
    public class ConfirmationItemResponse
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public ConfirmationStatusEnum NewStatus { get; set; }
        public DateTime? ConfirmedAt { get; set; }
        public string ConfirmedBy { get; set; }
    }

    public class BatchConfirmationResponse
    {
        public List<ConfirmationItemResponse> Items { get; set; } = new List<ConfirmationItemResponse>();
    }
}
