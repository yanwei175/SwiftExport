using System;

namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>取消采购确认并保留需求承接及确认历史；原因进入业务日志。</summary>
    public sealed class CancelProcurementOrderConfirmationCommand
    {
        /// <summary>本次操作身份；结果未知时保留原命令，后续权限拒绝不能证明首次未执行。</summary>
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
        public string Reason { get; set; }
    }
}
