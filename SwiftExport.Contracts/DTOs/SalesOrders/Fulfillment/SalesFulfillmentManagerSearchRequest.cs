using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment
{
    /// <summary>销售履约 Manager 的分页筛选条件。</summary>
    public class SalesFulfillmentManagerSearchRequest : PagedRequestBase
    {
        public string OrderNo { get; set; }
        public string CustomerCode { get; set; }
        public List<SalesOrderFulfillmentStatusEnum> FulfillmentStatuses { get; set; } = new List<SalesOrderFulfillmentStatusEnum>();
    }
}
