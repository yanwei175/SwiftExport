using System;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders.Trace
{
    /// <summary>采购聚合的追加式业务操作审计记录。</summary>
    public sealed class PurchaseOrderTraceOperationDto
    {
        public Guid Uid { get; set; }
        public string OperationName { get; set; }
        public string OperationType { get; set; }
        public string Reason { get; set; }
        public string Summary { get; set; }
        public string UserName { get; set; }
        public DateTime OccurredDate { get; set; }
    }
}
