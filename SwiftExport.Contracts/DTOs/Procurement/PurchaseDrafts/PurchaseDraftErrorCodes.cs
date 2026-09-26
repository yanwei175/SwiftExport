namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>采购草稿预览、创建与候选恢复使用的稳定错误代码。</summary>
    public static class PurchaseDraftErrorCodes
    {
        public const string PreviewExpired = "PREVIEW_EXPIRED";
        public const string PreviewFactChanged = "PREVIEW_FACT_CHANGED";
        public const string PreviewTokenMismatch = "PREVIEW_TOKEN_MISMATCH";
        public const string NetDemandNotFound = "NET_DEMAND_NOT_FOUND";
        public const string SourceNotFound = "SOURCE_NOT_FOUND";
    }
}
