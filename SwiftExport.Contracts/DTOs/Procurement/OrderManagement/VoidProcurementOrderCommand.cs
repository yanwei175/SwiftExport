using System;
namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>整单作废采购并解除本单承接，保留头行及历史事实。</summary>
    public sealed class VoidProcurementOrderCommand
    {
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
        public string Reason { get; set; }
    }
}
