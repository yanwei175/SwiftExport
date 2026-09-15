namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>定义装箱单查询与草稿命令的稳定错误代码。</summary>
    public static class PackingListErrorCodes
    {
        /// <summary>头行乐观并发冲突。</summary>
        public const string VersionConflict = "VERSION_CONFLICT";
        /// <summary>命令身份已被不同语义占用。</summary>
        public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
        /// <summary>完整行集合或显式删除集合不匹配。</summary>
        public const string LineSetMismatch = "LINE_SET_MISMATCH";
        /// <summary>草稿字段、数量或身份无效。</summary>
        public const string InvalidDraft = "INVALID_DRAFT";
        /// <summary>来源状态、产品身份或余额已变化。</summary>
        public const string SourceChanged = "SOURCE_CHANGED";
        /// <summary>来源客户与单头客户不一致。</summary>
        public const string CustomerMismatch = "CUSTOMER_MISMATCH";
        /// <summary>目标单据不存在。</summary>
        public const string NotFound = "NOT_FOUND";
        /// <summary>当前单据状态不允许执行命令。</summary>
        public const string StateBlocked = "STATE_BLOCKED";
        /// <summary>无法证明来源交付投影。</summary>
        public const string DeliveryUnavailable = "DELIVERY_QUERY_UNAVAILABLE";
        /// <summary>相同命令当前正由其他事务处理。</summary>
        public const string CommandBusy = "COMMAND_BUSY";
        /// <summary>历史命令结果不完整，不能安全重放。</summary>
        public const string OutcomeUnknown = "OUTCOME_UNKNOWN";
    }
}
