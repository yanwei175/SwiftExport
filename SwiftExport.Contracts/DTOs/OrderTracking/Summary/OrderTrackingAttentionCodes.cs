namespace SwiftExport.Contracts.DTOs.OrderTracking.Summary
{
    /// <summary>关注事项稳定代码；集合内为并集，与基础条件在同一需求上求交集。</summary>
    public static class OrderTrackingAttentionCodes
    {
        /// <summary>确定逾期。</summary>
        public const string Overdue = "Overdue";

        /// <summary>活动需求未指定交期。</summary>
        public const string MissingRequiredDate = "MissingRequiredDate";

        /// <summary>交付证据需核实。</summary>
        public const string DeliveryReview = "DeliveryReview";

        /// <summary>活动需求无活动物料，仅为事实提示。</summary>
        public const string NoActiveMaterial = "NoActiveMaterial";

        /// <summary>当前适用物料已确认采购不足，草稿占用不算已采购。</summary>
        public const string PurchaseShortfall = "PurchaseShortfall";

        /// <summary>当前适用物料已采购加草稿占用超量。</summary>
        public const string PurchaseExcess = "PurchaseExcess";

        /// <summary>当前采购关联或物料依据需核实。</summary>
        public const string PurchaseReview = "PurchaseReview";

        /// <summary>关联采购整行未收足。</summary>
        public const string ReceiptOutstanding = "ReceiptOutstanding";

        /// <summary>关联采购整行超收。</summary>
        public const string ReceiptExcess = "ReceiptExcess";

        /// <summary>当前关联采购整行收货需核实。</summary>
        public const string ReceiptReview = "ReceiptReview";

        /// <summary>来源身份或快照需核实。</summary>
        public const string SourceReview = "SourceReview";
    }
}
