namespace SwiftExport.Contracts.SysEnums.StaticEnums
{
    /// <summary>
    /// 单据首字母类型枚举
    /// </summary>
    public static class DocumentTypeStaticsEnum
    {
        /// <summary>
        /// 销售订单。
        /// </summary>
        public const string SalesOrder = "SO";

        /// <summary>
        /// 手工毛需求。
        /// </summary>
        public const string ManualDemand = "MD";

        /// <summary>
        /// 投诉补货需求。
        /// </summary>
        public const string ComplaintReplenishment = "CR";

        /// <summary>
        /// 装箱单。
        /// </summary>
        public const string PackingList = "PL";

        /// <summary>
        /// 采购订单。
        /// </summary>
        public const string PurchaseOrder = "PO";

        /// <summary>入库单，按收货年月分段生成流水号。</summary>
        public const string InboundDocument = "IR";
        /// <summary>普通库存出库单。</summary>
        public const string OutboundDocument = "OI";

        /// <summary>
        /// 生产单（Manufacturing Order）。
        /// </summary>
        public const string ManufacturingOrder = "MO";

    }
}
