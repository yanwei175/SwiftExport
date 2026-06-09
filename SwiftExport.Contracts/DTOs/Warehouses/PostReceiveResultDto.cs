using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.DTOs.Warehouses
{
    public class PostReceiveResultDto
    {
        public Guid Uid { get; set; }
        public string ReceiveNo { get; set; } = string.Empty;
    }
}
