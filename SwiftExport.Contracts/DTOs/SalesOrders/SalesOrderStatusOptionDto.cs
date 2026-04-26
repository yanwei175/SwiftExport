namespace SwiftExport.Contracts.DTOs.SalesOrders
{
    /// <summary>
    /// 中文注释：订单筛选状态选项 DTO。
    /// 用于多选筛选控件展示（CustomerName）和回传（Value）。
    /// </summary>
    public class SalesOrderStatusOptionDto
    {
        /// <summary>
        /// 展示名称（中文）
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 实际值（建议与后端枚举/字典编码一致）
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// 可选：排序号
        /// </summary>
        public int Sort { get; set; }
    }
}
