using SwiftExport.Contracts.SysEnums;
using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Detail
{
    /// <summary>销售订单确认或取消确认后的状态、版本和成品需求处理结果。</summary>
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
