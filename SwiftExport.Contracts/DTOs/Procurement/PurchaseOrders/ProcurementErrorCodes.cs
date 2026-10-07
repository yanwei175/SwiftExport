namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>定义采购命令和产品匹配的专用错误；导入检查可携带同义代码而保持正常响应。</summary>
    public static class ProcurementErrorCodes
    {
        /// <summary>采购确认依据的 BOM 指纹已变化。</summary>
        public const string BomBasisChanged = "PROC_BOM_BASIS_CHANGED";
        /// <summary>当前客户型号无可匹配产品关系。</summary>
        public const string ProductNotMatched = "ProductNotMatched";
        /// <summary>提交的所选关系不属于当前客户型号候选。</summary>
        public const string SelectionInvalid = "SelectionInvalid";
        /// <summary>当前客户型号具有多个候选而未明确选择。</summary>
        public const string ProductAmbiguous = "ProductAmbiguous";
        /// <summary>当前关系或产品不可采购，或缺少可用基础单位。</summary>
        public const string ProductUnavailable = "ProductUnavailable";
    }
}
