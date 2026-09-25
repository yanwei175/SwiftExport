using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Manager
{
    /// <summary>销售订单 Manager 的分页、状态和业务字段筛选条件。</summary>
    public class SalesOrderManagerSearchRequest : PagedRequestBase
    {
        public Guid? CustomerId { get; set; }
        public List<string> CustomerCodes { get; set; } = new List<string>();
        public string OrderNo { get; set; }
        public string CustomerPoNo { get; set; }
        public string InvoiceNo { get; set; }
        public DateTime? OrderDateFrom { get; set; }
        public DateTime? OrderDateTo { get; set; }
        public DateTime? DeliveryDateFrom { get; set; }
        public DateTime? DeliveryDateTo { get; set; }
        public List<string> CustomerSkus { get; set; } = new List<string>();
        public List<SalesOrderDocumentStatusEnum> DocumentStatuses { get; set; } = new List<SalesOrderDocumentStatusEnum>();
        public List<ConfirmationStatusEnum> ConfirmationStatuses { get; set; } = new List<ConfirmationStatusEnum>();
    }
}
