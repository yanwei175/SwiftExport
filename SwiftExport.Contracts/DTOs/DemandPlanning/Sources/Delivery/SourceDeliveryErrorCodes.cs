namespace SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery
{
    /// <summary>定义来源交付查询和额度复核代码；共享交付不可用码供箱单兼容成员引用。</summary>
    public static class SourceDeliveryErrorCodes
    {
        /// <summary>数据库结构或历史事实不足以证明交付净额。</summary>
        public const string QueryUnavailable = "DELIVERY_QUERY_UNAVAILABLE";
        /// <summary>查询参数或额度声明格式无效。</summary>
        public const string ClaimInvalid = "SOURCE_DELIVERY_CLAIM_INVALID";
        /// <summary>指定的来源需求不存在或已删除。</summary>
        public const string NotFound = "SOURCE_DELIVERY_NOT_FOUND";
        /// <summary>来源需求已关闭、作废或缺少交付客户。</summary>
        public const string Inactive = "SOURCE_DELIVERY_INACTIVE";
        /// <summary>来源需求或上游事实版本已经变化。</summary>
        public const string VersionConflict = "SOURCE_DELIVERY_VERSION_CONFLICT";
        /// <summary>聚合申报数量超过当前剩余可交付量。</summary>
        public const string Exceeded = "SOURCE_DELIVERY_EXCEEDED";
        /// <summary>来源交付查询条件或来源身份无效。</summary>
        public const string SourceDeliveryQueryInvalid = "SOURCE_DELIVERY_QUERY_INVALID";
        /// <summary>来源退出所需的历史交付事实无法证明。</summary>
        public const string SourceDeliveryHistoryInvalid = "SOURCE_DELIVERY_HISTORY_INVALID";
        /// <summary>来源仍有净交付事实，不能退出。</summary>
        public const string SourceDeliveryExists = "SOURCE_DELIVERY_EXISTS";
    }
}
