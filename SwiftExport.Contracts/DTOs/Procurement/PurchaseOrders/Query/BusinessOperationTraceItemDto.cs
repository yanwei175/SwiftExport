using System;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders
{
    /// <summary>
    /// 采购业务操作轨迹项，用于展示确认、取消确认和供应商跟进等已成功业务命令的审计信息。
    /// </summary>
    /// <remarks>来源表：BusinessOperationLog。</remarks>
    public class BusinessOperationTraceItemDto
    {
        public Guid Uid { get; set; }
        public string OperationType { get; set; }
        public string OperationName { get; set; }
        public string Reason { get; set; }
        public string Summary { get; set; }
        public string ChangedFieldsJson { get; set; }
        public string RelatedObjectsJson { get; set; }
        public string UserName { get; set; }
        public DateTime OccurredDate { get; set; }
        public string CorrelationId { get; set; }
    }

}
