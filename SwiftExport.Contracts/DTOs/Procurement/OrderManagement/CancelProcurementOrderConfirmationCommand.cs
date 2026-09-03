using System;

namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>取消独立采购确认；原因进入业务日志，已冻结确认快照保留为历史依据。</summary>
    public sealed class CancelProcurementOrderConfirmationCommand
    {
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
        public string Reason { get; set; }
    }
}
