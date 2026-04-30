namespace SwiftExport.Contracts.SysEnums.StaticEnums
{
    /// <summary>
    /// 单据类型字符串枚举（禁止使用 C# enum）。
    /// 用于单号规则、流程分发与权限控制等场景。
    /// </summary>
    public static class DocumentTypeStaticEnums
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
        public const string RequirementPool = "RP";
    }
}
