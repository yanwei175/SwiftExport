namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>定义箱单草稿、过账和反向的代码；预览阻断保留来源和数量信息，只有命令失败进入 HTTP 映射。</summary>
    public static class PackingListErrorCodes
    {
        /// <summary>头行乐观并发冲突。</summary>
        public const string VersionConflict = SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.VersionConflict;
        /// <summary>命令身份已被不同语义占用。</summary>
        public const string CommandConflict = SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.CommandConflict;
        /// <summary>完整行集合或显式删除集合不匹配。</summary>
        public const string LineSetMismatch = "LINE_SET_MISMATCH";
        /// <summary>草稿字段、数量或身份无效。</summary>
        public const string InvalidDraft = "INVALID_DRAFT";
        /// <summary>来源状态、产品身份或余额已变化。</summary>
        public const string SourceChanged = "SOURCE_CHANGED";
        /// <summary>来源客户与单头客户不一致。</summary>
        public const string CustomerMismatch = "CUSTOMER_MISMATCH";
        /// <summary>目标单据不存在。</summary>
        public const string NotFound = SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.NotFound;
        /// <summary>当前单据状态不允许执行命令。</summary>
        public const string StateBlocked = SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.StateBlocked;
        /// <summary>无法证明来源交付投影。</summary>
        public const string DeliveryUnavailable = SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery.SourceDeliveryErrorCodes.QueryUnavailable;
        /// <summary>相同命令当前正由其他事务处理。</summary>
        public const string CommandBusy = SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.CommandBusy;
        /// <summary>历史命令结果不完整，不能安全重放。</summary>
        public const string OutcomeUnknown = SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.OutcomeUnknown;
        /// <summary>箱单行身份或数量无效。</summary>
        public const string InvalidLines = "INVALID_LINES";
        /// <summary>箱单当前状态不支持本次过账或反向。</summary>
        public const string InvalidState = "INVALID_STATE";
        /// <summary>正式提交时过账或反向预览依据已变化。</summary>
        public const string PreviewStale = "PREVIEW_STALE";
        /// <summary>来源事实具有重复身份，不能分配。</summary>
        public const string AmbiguousSource = "AMBIGUOUS_SOURCE";
        /// <summary>相关库存余额缺失或单位异常。</summary>
        public const string BalanceInvalid = "BALANCE_INVALID";
        /// <summary>客供件采用依据无效。</summary>
        public const string ComponentInvalid = "COMPONENT_INVALID";
        /// <summary>可用成品库存或关联采购容量不足。</summary>
        public const string SupplyShortfall = "SUPPLY_SHORTFALL";
        /// <summary>客供件可用库存不足。</summary>
        public const string ComponentShortfall = "COMPONENT_SHORTFALL";
        /// <summary>箱单客户不存在或无效。</summary>
        public const string CustomerInvalid = "CUSTOMER_INVALID";
        /// <summary>箱单累计出货超过来源剩余量。</summary>
        public const string DeliveryExceeded = "DELIVERY_EXCEEDED";
        /// <summary>来源具有多份活动根需求。</summary>
        public const string DemandAmbiguous = "DEMAND_AMBIGUOUS";
        /// <summary>来源缺少有效需求、单位或采购执行依据。</summary>
        public const string DemandInvalid = "DEMAND_INVALID";
        /// <summary>物料采用依据与当前来源不一致。</summary>
        public const string DemandStale = "DEMAND_STALE";
        /// <summary>同一需求身份被重复耗用。</summary>
        public const string DuplicateDemand = "DUPLICATE_DEMAND";
        /// <summary>采购执行关系不能证明当前来源容量。</summary>
        public const string ExecutionLinkInvalid = "EXECUTION_LINK_INVALID";
        /// <summary>撤销原自动入库后库存将不足。</summary>
        public const string NegativeStock = "NEGATIVE_STOCK";
        /// <summary>箱单来源产品不存在或不可用。</summary>
        public const string ProductInvalid = "PRODUCT_INVALID";
        /// <summary>箱单来源不存在或已失效。</summary>
        public const string SourceInvalid = "SOURCE_INVALID";
        /// <summary>箱单仍有未关联来源的行，不能正式过账。</summary>
        public const string SourceNotAssociated = "SOURCE_NOT_ASSOCIATED";
        /// <summary>原执行或端口返回证据缺失、重复或不一致。</summary>
        public const string ExecutionInvalid = "EXECUTION_INVALID";
        /// <summary>提交后无法获得完整箱单详情。</summary>
        public const string ResultMissing = "RESULT_MISSING";
        /// <summary>箱单业务审计未写入。</summary>
        public const string LogFailed = "LOG_FAILED";
        /// <summary>箱单执行不能接管已有事务。</summary>
        public const string TransactionExists = "TRANSACTION_EXISTS";
        /// <summary>内部执行提前终止了外层事务。</summary>
        public const string TransactionAborted = "TRANSACTION_ABORTED";
        /// <summary>反向的自动单据与原执行证据不完整对应。</summary>
        public const string DocumentIntegrity = "DOCUMENT_INTEGRITY";
    }
}
