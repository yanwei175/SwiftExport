using SwiftExport.Contracts.Wrappers;
using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Requests
{
    /// <summary>
    /// 销售订单查询搜索条件 DTO。
    /// 用于前端向后端发送查询请求时的条件封装，支持多条件组合搜索。
    /// </summary>
    public class SalesOrderSearchRequest : PagedRequestBase
    {


        // 中文注释：订单号（模糊匹配）
        public string OrderNo { get; set; }

        // 中文注释：客户 ID（精确匹配）
        public Guid? CustomerId { get; set; }

        // 中文注释：客户名称（模糊匹配）
        public string CustomerName { get; set; }

        // 中文注释：下单日期范围 - 开始日期
        public DateTime? OrderDateFrom { get; set; }

        // 中文注释：下单日期范围 - 结束日期
        public DateTime? OrderDateTo { get; set; }

        // 中文注释：期望交期范围 - 开始日期
        public DateTime? DeliveryDateFrom { get; set; }

        // 中文注释：期望交期范围 - 结束日期
        public DateTime? DeliveryDateTo { get; set; }

        // 中文注释：订单状态
        public int OrderStatus { get; set; }

        // 中文注释：订单总金额范围 - 最小值
        public decimal? TotalAmountFrom { get; set; }

        // 中文注释：订单总金额范围 - 最大值
        public decimal? TotalAmountTo { get; set; }

        // 中文注释：排序字段（默认按下单日期倒序）
        public string SortBy { get; set; } = "OrderDate";

        // 中文注释：排序方向（true=升序, false=降序）
        public bool SortAscending { get; set; } = false;
    }
}
