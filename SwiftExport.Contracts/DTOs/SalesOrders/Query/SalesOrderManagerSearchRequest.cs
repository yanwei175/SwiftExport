using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Manager
{
    /// <summary>销售订单事实的分页、生命周期和商业字段筛选，不包含履约进度条件。</summary>
    public class SalesOrderManagerSearchRequest : PagedRequestBase
    {
        public Guid? CustomerId { get; set; }
        public List<string> CustomerCodes { get; set; } = new List<string>();
        public string OrderNo { get; set; }
        public string CustomerPoNo { get; set; }
        public string InvoiceNo { get; set; }
        public DateTime? OrderDateFrom { get; set; }
        public DateTime? OrderDateTo { get; set; }
        /// <summary>按活动销售行有效交期筛选的起始日期，行交期优先于头交期。</summary>
        public DateTime? EffectiveDeliveryDateFrom { get; set; }
        /// <summary>按活动销售行有效交期筛选的结束日期（含）。</summary>
        public DateTime? EffectiveDeliveryDateTo { get; set; }
        public List<string> CustomerSkus { get; set; } = new List<string>();
        public List<SalesOrderDocumentStatusEnum> DocumentStatuses { get; set; } = new List<SalesOrderDocumentStatusEnum>();
        public List<ConfirmationStatusEnum> ConfirmationStatuses { get; set; } = new List<ConfirmationStatusEnum>();
    }
}
