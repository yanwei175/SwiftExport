using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.SalesOrders
{
    /// <summary>
    /// 中文注释：订单管理页主表查询 DTO。
    /// 用于 SalesOrderManagerView 的左侧筛选条件，支持多条件组合查询。
    /// </summary>
    public class SalesOrderManagerSearchDto : PagedRequestDto
    {

        /// <summary>
        /// 客户代码（左侧客户筛选）
        /// </summary>
        public string CustomerCode { get; set; }

        /// <summary>
        /// 审批状态多选
        /// </summary>
        public List<int> ApprovalStatuses { get; set; } = new List<int>();

        /// <summary>
        /// 订单状态多选
        /// </summary>
        public List<int> OrderStatuses { get; set; } = new List<int>();

        /// <summary>
        /// 订单号筛选
        /// </summary>
        public string OrderNo { get; set; }

        /// <summary>
        /// 发票号筛选
        /// </summary>
        public string InvoiceNo { get; set; }

        /// <summary>
        /// 精确查询单条订单（用于弹窗保存后的局部刷新），为 null 时不参与筛选。
        /// </summary>
        public int? OrderId { get; set; }

        /// <summary>
        /// 包含产品型号（多行输入，后端可按换行/逗号分词）
        /// </summary>
        public List<string> BatchCustomerSkuList { get; set; } = new List<string>();

        /// <summary>
        /// 下单日期范围（开始）
        /// </summary>
        public DateTime? OrderDateFrom { get; set; }

        /// <summary>
        /// 下单日期范围（结束）
        /// </summary>
        public DateTime? OrderDateTo { get; set; }

        /// <summary>
        /// 排序字段
        /// </summary>
        public string SortBy { get; set; }

        /// <summary>
        /// 是否升序（false=降序）
        /// </summary>
        public bool SortAscending { get; set; } = false;
    }
}
