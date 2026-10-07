namespace SwiftExport.Contracts.DTOs.ForExcel.PackingLists
{
    /// <summary>定义箱单正常导入检查响应中的问题与确认警告代码，不代表请求执行失败。</summary>
    public static class PackingListImportIssueCodes
    {
        /// <summary>箱单导入客户不可用。</summary>
        public const string CustomerUnavailable = "CustomerUnavailable";
        /// <summary>箱单导入缺少单据日期。</summary>
        public const string DateRequired = "DateRequired";
        /// <summary>箱单导入文本超过字段长度。</summary>
        public const string TextTooLong = "TextTooLong";
        /// <summary>箱单导入数量或精度无效。</summary>
        public const string QuantityInvalid = "QuantityInvalid";
        /// <summary>箱单导入包装数值或精度无效。</summary>
        public const string PackingInvalid = "PackingInvalid";
        /// <summary>非标箱单行不能携带标准产品。</summary>
        public const string NonStandardProduct = "NonStandardProduct";
        /// <summary>非标箱单行缺少描述。</summary>
        public const string DescriptionRequired = "DescriptionRequired";
        /// <summary>箱单行或系统产品缺少单位。</summary>
        public const string UnitRequired = "UnitRequired";
        /// <summary>箱单导入无法明确匹配客户产品。</summary>
        public const string ProductNotMatched = "ProductNotMatched";
        /// <summary>箱单导入所选产品或客户关系不可用。</summary>
        public const string ProductUnavailable = "ProductUnavailable";
        /// <summary>箱单导入中性型号与系统不同，需要确认。</summary>
        public const string LinkSkuMismatch = "LinkSkuMismatch";
    }
}
