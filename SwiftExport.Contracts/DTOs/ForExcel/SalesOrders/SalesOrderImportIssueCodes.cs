namespace SwiftExport.Contracts.DTOs.ForExcel.SalesOrders
{
    /// <summary>定义销售订单正常导入检查响应中的问题与确认警告代码，不代表请求执行失败。</summary>
    public static class SalesOrderImportIssueCodes
    {
        /// <summary>客户不可用或代码存在歧义。</summary>
        public const string CustomerUnavailable = "CustomerUnavailable";
        /// <summary>检查产品前必须定位客户。</summary>
        public const string CustomerRequired = "CustomerRequired";
        /// <summary>客户已有同发票或 PO 的订单，需要明确确认。</summary>
        public const string ExistingOrders = "ExistingOrders";
        /// <summary>所选关系不属于当前客户型号。</summary>
        public const string SelectionInvalid = "SelectionInvalid";
        /// <summary>客户型号没有候选产品关系。</summary>
        public const string ProductNotMatched = "ProductNotMatched";
        /// <summary>客户型号存在多个候选，须明确选择。</summary>
        public const string ProductAmbiguous = "ProductAmbiguous";
        /// <summary>关联或产品不可用或缺少单位。</summary>
        public const string ProductUnavailable = "ProductUnavailable";
        /// <summary>导入中性型号与系统资料不同，需要确认。</summary>
        public const string LinkSkuMismatch = "LinkSkuMismatch";
        /// <summary>导入单头缺少订单日期。</summary>
        public const string OrderDateRequired = "OrderDateRequired";
        /// <summary>导入汇率不是正数。</summary>
        public const string ExchangeRateInvalid = "ExchangeRateInvalid";
        /// <summary>导入币种未明确选择。</summary>
        public const string CurrencyRequired = "CurrencyRequired";
        /// <summary>导入定金比例不在允许范围。</summary>
        public const string DepositRateInvalid = "DepositRateInvalid";
        /// <summary>非标导入行缺少描述。</summary>
        public const string DescriptionRequired = "DescriptionRequired";
        /// <summary>非标导入行缺少单位。</summary>
        public const string UnitRequired = "UnitRequired";
        /// <summary>导入数量不是正数。</summary>
        public const string QuantityInvalid = "QuantityInvalid";
        /// <summary>导入每箱数量无效。</summary>
        public const string PackingInvalid = "PackingInvalid";
        /// <summary>导入价格或包装数值为负数。</summary>
        public const string NumberNegative = "NumberNegative";
        /// <summary>导入数量与价格乘积溢出。</summary>
        public const string AmountOverflow = "AmountOverflow";
    }
}
