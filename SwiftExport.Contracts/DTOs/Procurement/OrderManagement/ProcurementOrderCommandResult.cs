using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>命令执行时冻结的订单结果；幂等重放返回原结果，不重建当前详情。</summary>
    public sealed class ProcurementOrderCommandResult
    {
        public Guid PurchaseOrderId { get; set; }
        public string OrderNo { get; set; }
        public int Version { get; set; }
        public ConfirmationStatusEnum ConfirmationStatus { get; set; }
        public Guid? ConfirmationId { get; set; }
        public List<ProcurementLineVersionDto> Lines { get; set; } = new List<ProcurementLineVersionDto>();
    }
}
