namespace SwiftExport.Contracts.DTOs.SalesOrders
{
    /// <summary>
    /// 销售毛需求拆解请求 DTO。
    /// </summary>
    public class IndependentDemandDecomposeRequestDto
    {
        /// <summary>
        /// 来源单据类型（如：销售订单）。
        /// </summary>
        public string SourceType { get; set; } = string.Empty;

        /// <summary>
        /// 来源单号。
        /// </summary>
        public string SourceNo { get; set; } = string.Empty;
    }
}
