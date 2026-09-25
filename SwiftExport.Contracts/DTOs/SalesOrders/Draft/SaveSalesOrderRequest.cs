using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Detail
{
    /// <summary>保存销售订单草稿时提交的完整订单头、活动行和删除行集合。</summary>
    public class SaveSalesOrderRequest
    {
        /// <summary>本次创建或更新意图的稳定标识；重试必须复用。</summary>
        public Guid CommandId { get; set; }
        public SalesOrderEditHeaderDto Header { get; set; }
        public List<SalesOrderEditLineDto> Lines { get; set; } = new List<SalesOrderEditLineDto>();
        public List<SalesOrderDeletedLineDto> DeletedLines { get; set; } = new List<SalesOrderDeletedLineDto>();
    }
}
