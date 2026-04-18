using System;

namespace SwiftExport.View.FW.SalesOrderViews.Models
{
    /// <summary>
    /// 销售订单查询过滤条件模型。
    /// 用于主表列表页的高级搜索和过滤，支持订单号、客户、日期范围、状态等条件组合。
    /// </summary>
    public class SalesOrderQueryFilter
    {
        // 中文注释：订单号（模糊匹配）
        public string OrderNo { get; set; }

        // 中文注释：客户 ID，用于精确匹配某个客户的所有订单
        public int? CustomerId { get; set; }

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

        // 中文注释：订单状态（待审核、已审核、已发货、已收货、已取消等）
        public string OrderStatus { get; set; }

        // 中文注释：订单总金额范围 - 最小值
        public decimal? TotalAmountFrom { get; set; }

        // 中文注释：订单总金额范围 - 最大值
        public decimal? TotalAmountTo { get; set; }

        // 中文注释：分页信息 - 当前页码（从 1 开始）
        public int PageIndex { get; set; } = 1;

        // 中文注释：分页信息 - 每页记录数
        public int PageSize { get; set; } = 50;

        // 中文注释：通用搜索关键字（可对订单号、客户名称等多字段模糊搜索）
        public string Keyword { get; set; }

        // 中文注释：排序字段（默认按下单日期倒序）
        public string SortBy { get; set; } = "OrderDate";

        // 中文注释：排序方向（true=升序, false=降序）
        public bool SortAscending { get; set; } = false;

        /// <summary>
        /// 中文注释：重置所有过滤条件为默认值（清空所有搜索条件，恢复分页为第一页）。
        /// </summary>
        public void Reset()
        {
            OrderNo = null;
            CustomerId = null;
            CustomerName = null;
            OrderDateFrom = null;
            OrderDateTo = null;
            DeliveryDateFrom = null;
            DeliveryDateTo = null;
            OrderStatus = null;
            TotalAmountFrom = null;
            TotalAmountTo = null;
            Keyword = null;
            PageIndex = 1;
            PageSize = 50;
            SortBy = "OrderDate";
            SortAscending = false;
        }

        /// <summary>
        /// 中文注释：判断是否有任何过滤条件被设置（不包括分页和排序信息）。
        /// 用于 UI 判断是否需要显示"清空条件"按钮。
        /// </summary>
        public bool HasAnyFilter()
        {
            return !string.IsNullOrWhiteSpace(OrderNo)
                || CustomerId.HasValue
                || !string.IsNullOrWhiteSpace(CustomerName)
                || OrderDateFrom.HasValue
                || OrderDateTo.HasValue
                || DeliveryDateFrom.HasValue
                || DeliveryDateTo.HasValue
                || !string.IsNullOrWhiteSpace(OrderStatus)
                || TotalAmountFrom.HasValue
                || TotalAmountTo.HasValue
                || !string.IsNullOrWhiteSpace(Keyword);
        }
    }
}
