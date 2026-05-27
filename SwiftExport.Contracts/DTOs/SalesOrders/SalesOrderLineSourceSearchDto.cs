namespace SwiftExport.Contracts.DTOs.SalesOrders
{
    /// <summary>
    /// 销售订单行数据源分页查询参数。
    /// 用于从“客户产品 + 产品”联查结果中检索可下单行数据。
    /// </summary>
    public class SalesOrderLineSourceSearchDto
    {
        /// <summary>
        /// 关键字（匹配 SKU / 客户型号 / 客户描述）。
        /// </summary>
        public string Keyword { get; set; }

        /// <summary>
        /// 批量关键字（支持换行或逗号分隔）。
        /// </summary>
        public string BatchKeywords { get; set; }

        /// <summary>
        /// 客户SKU精确匹配（支持换行或逗号分隔）。
        /// 用于“客户型号精确匹配”场景，和 Keyword 的模糊检索互补。
        /// </summary>
        public string ExactCustomerSkuBatch { get; set; }

        /// <summary>
        /// 客户 Id（必填）。
        /// </summary>
        public int CustomerId { get; set; }

        /// <summary>
        /// 客户编码（可选，作为补充过滤条件）。
        /// </summary>
        public string CustomerCode { get; set; }

        /// <summary>
        /// 页码，从 1 开始。
        /// </summary>
        public int PageIndex { get; set; } = 1;

        /// <summary>
        /// 每页条数。
        /// </summary>
        public int PageSize { get; set; } = 20;
    }
}
