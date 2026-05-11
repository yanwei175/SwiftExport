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
        /// 采购订单。
        /// </summary>
        public const string PurchaseOrder = "PO";

        /// <summary>
        /// 生产单（Manufacturing Order）。
        /// </summary>
        public const string ManufacturingOrder = "MO";

        /// <summary>
        /// 需求池/拆解池。
        /// </summary>
        public const string DependentDemand = "RP";

        /// <summary>
        /// 入库单
        /// </summary> 
        public const string InboundOrder = "IB";

        /// <summary>
        /// 出库单
        /// </summary> 
        public const string OutboundOrder = "OB";

        /// <summary>
        /// 非标件编号,很多地方基于这个开头判断是否为非标件，所以这个值不能随意修改
        /// </summary> 
        public const string NonStandardPart = "非标件";


    }
}
