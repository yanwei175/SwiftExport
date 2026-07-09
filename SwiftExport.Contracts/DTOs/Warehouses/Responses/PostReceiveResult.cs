using System;

namespace SwiftExport.Contracts.DTOs.Warehouses.Responses
{
    public class PostReceiveResult
    {
        public Guid Uid { get; set; }
        public string ReceiveNo { get; set; } = string.Empty;
    }
}
