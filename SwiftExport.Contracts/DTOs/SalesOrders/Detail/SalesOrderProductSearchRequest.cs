using SwiftExport.Contracts.Wrappers;
using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Detail
{
    /// <summary>销售订单详情产品 Lookup 的客户范围和分页搜索条件。</summary>
    public class SalesOrderProductSearchRequest : PagedRequestBase
    {
        public Guid? CustomerId { get; set; }
    }
}
