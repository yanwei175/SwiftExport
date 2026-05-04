namespace SwiftExport.Contracts.DTOs.Orders
{
    /// <summary>
    /// 执行采购入库行请求 DTO。
    /// </summary>
    public class PurchaseReceiveExecuteLineDto
    {
        /// <summary>
        /// 物料 SKU。
        /// </summary>
        public string Sku { get; set; } = string.Empty;

        /// <summary>
        /// 本次入库数量。
        /// </summary>
        public decimal Qty { get; set; }

        /// <summary>
        /// 单位。
        /// </summary>
        public string Unit { get; set; }

        /// <summary>
        /// 关联采购单行ID（用于回写采购行 RecQty）。
        /// </summary>
        public long? PoLineId { get; set; }

        /// <summary>
        /// 关联销售单行ID（用于回写需求池 ReceivedQty）。
        /// </summary>
        public long? SoLineId { get; set; }

        /// <summary>
        /// 来源类型（用于回写 IndependentDemand 时避免硬编码来源）。
        /// </summary>
        public string SourceType { get; set; } = string.Empty;
    }
}
