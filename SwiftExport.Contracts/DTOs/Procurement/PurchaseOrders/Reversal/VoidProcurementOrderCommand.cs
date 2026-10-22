using System;
namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>整单作废采购并解除本单承接，保留头行及历史事实。</summary>
    public sealed class VoidProcurementOrderCommand
    {
        /// <summary>本次操作身份；结果未知时保留原命令，后续权限拒绝不能证明首次未执行。</summary>
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
        public string Reason { get; set; }
    }
}
