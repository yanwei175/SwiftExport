namespace SwiftExport.Contracts.DTOs.QualityControl
{
    /// <summary>定义售后模块专用拒绝代码；通用权限、版本和状态错误继续使用 ServiceErrorCodes。</summary>
    public static class AfterSalesErrorCodes
    {
        /// <summary>售后结案缺少可证明的交付查询能力。</summary>
        public const string AftersalesDeliveryQueryUnavailable = "AFTERSALES_DELIVERY_QUERY_UNAVAILABLE";
    }
}
