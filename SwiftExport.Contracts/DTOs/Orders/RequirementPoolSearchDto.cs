namespace SwiftExport.Contracts.DTOs.Orders
{
    /// <summary>
    /// 需求池分页查询参数。
    /// </summary>
    public class DependentDemandSearchDto
    {
        /// <summary>
        /// 关键字（匹配来源单号/SKU/目标执行单号）。
        /// </summary>
        public string Keyword { get; set; }

        /// <summary>
        /// 来源单号。
        /// </summary>
        public string SourceNo { get; set; }

        /// <summary>
        /// 来源类型（SO/样品等）。
        /// </summary>
        public string SourceType { get; set; }

        /// <summary>
        /// SKU。
        /// </summary>
        public string Sku { get; set; }

        /// <summary>
        /// 供货类型。
        /// </summary>
        public string SupplyType { get; set; }

        /// <summary>
        /// 层级过滤（0=成品，1=子件，null=全部）。
        /// </summary>
        public int? BomLevel { get; set; }

        /// <summary>
        /// 供应商关键字（编码/名称）。
        /// </summary>
        public string SupplierKeyword { get; set; }

        /// <summary>
        /// 状态标记（RequirementStatusEnums）。
        /// </summary>
        public string StatusFlag { get; set; }

        /// <summary>
        /// 是否齐套。
        /// </summary>
        public bool? IsKitted { get; set; }

        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }
}
