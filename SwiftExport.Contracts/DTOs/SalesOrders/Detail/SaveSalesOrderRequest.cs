using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Detail
{
    /// <summary>保存销售订单草稿时提交的完整订单头、活动行和删除行集合。</summary>
    public class SaveSalesOrderRequest
    {
        public SalesOrderEditHeaderDto Header { get; set; }
        public List<SalesOrderEditLineDto> Lines { get; set; } = new List<SalesOrderEditLineDto>();
        public List<SalesOrderDeletedLineDto> DeletedLines { get; set; } = new List<SalesOrderDeletedLineDto>();
    }
}
