using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.SalesOrders
{
    /// <summary>
    /// 中文注释：订单管理页主表查询 DTO。
    /// 用于 SalesOrderManagerView 的左侧筛选条件，支持多条件组合查询。
    /// </summary>
    public class SalesOrderManagerSearchDto
    {
        /// <summary>
        /// 模糊关键词（可用于订单号/客户代码/备注等后端统一检索字段）
        /// </summary>
        public string Keyword { get; set; }

        /// <summary>
        /// 客户代码（左侧客户筛选）
        /// </summary>
        public string CustomerCode { get; set; }

        /// <summary>
        /// 审批状态多选
        /// </summary>
        public List<string> ApprovalStatuses { get; set; } = new List<string>();

        /// <summary>
        /// 订单状态多选
        /// </summary>
        public List<string> OrderStatuses { get; set; } = new List<string>();

        /// <summary>
        /// 订单号筛选
        /// </summary>
        public string OrderNo { get; set; }

        /// <summary>
        /// 发票号筛选
        /// </summary>
        public string InvoiceNo { get; set; }

        /// <summary>
        /// 产品颜色筛选（建议传 ColorCodeUnique）
        /// </summary>
        public string ProductColorCode { get; set; }

        /// <summary>
        /// 包含产品型号（多行输入，后端可按换行/逗号分词）
        /// </summary>
        public string IncludeSkus { get; set; }

        /// <summary>
        /// 下单日期范围（开始）
        /// </summary>
        public DateTime? OrderDateFrom { get; set; }

        /// <summary>
        /// 下单日期范围（结束）
        /// </summary>
        public DateTime? OrderDateTo { get; set; }

        /// <summary>
        /// 分页：页码（从 1 开始）
        /// </summary>
        public int PageIndex { get; set; } = 1;

        /// <summary>
        /// 分页：每页条数
        /// </summary>
        public int PageSize { get; set; } = 50;

        /// <summary>
        /// 排序字段
        /// </summary>
        public string SortBy { get; set; } = "OrderDate";

        /// <summary>
        /// 是否升序（false=降序）
        /// </summary>
        public bool SortAscending { get; set; } = false;
    }
}
