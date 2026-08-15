using SwiftExport.Contracts.SysEnums;
using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Workspace
{
    public class SalesOrderConfirmationResultDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public ConfirmationStatusEnum ConfirmationStatus { get; set; }
        public int GrossDemandCount { get; set; }
        public int HardDeletedGrossDemandCount { get; set; }
        public int VoidedGrossDemandCount { get; set; }
    }
}
