namespace SwiftExport.Contracts.DTOs.Errors
{
    /// <summary>
    /// 定义跨业务模块稳定复用的服务错误代码；模块专属错误应保留在各自契约边界。
    /// </summary>
    public static class ServiceErrorCodes
    {
        /// <summary>缺少有效身份。</summary>
        public const string Unauthenticated = "UNAUTHENTICATED";
        /// <summary>当前身份没有操作权限。</summary>
        public const string Forbidden = "FORBIDDEN";
        /// <summary>输入参数缺失或格式无效。</summary>
        public const string Validation = "VALIDATION";
        /// <summary>期望版本与当前版本冲突。</summary>
        public const string VersionConflict = "VERSION_CONFLICT";
        /// <summary>命令标识已被其他内容或意图占用。</summary>
        public const string CommandConflict = "COMMAND_CONFLICT";
        /// <summary>业务规则阻止操作。</summary>
        public const string BusinessBlocked = "BUSINESS_BLOCKED";
        /// <summary>未预期的服务端错误。</summary>
        public const string Unexpected = "UNEXPECTED";
        /// <summary>目标资源不存在。</summary>
        public const string NotFound = "NOT_FOUND";
        /// <summary>当前状态不允许执行。</summary>
        public const string StateBlocked = "STATE_BLOCKED";
        /// <summary>同一命令正在处理，应保留原命令。</summary>
        public const string CommandBusy = "COMMAND_BUSY";
        /// <summary>请求频率超过限制。</summary>
        public const string RateLimited = "RATE_LIMITED";
        /// <summary>服务暂不可用，不证明写入未提交。</summary>
        public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";
        /// <summary>无法确认提交结果，应保留原命令。</summary>
        public const string OutcomeUnknown = "OUTCOME_UNKNOWN";
        /// <summary>命令身份或必填输入无效；保留遗留字符串，区别于通用字段验证码。</summary>
        public const string InvalidCommand = "INVALID_COMMAND";
        /// <summary>仓储或遗留服务检测到版本并发冲突；不改写为 VERSION_CONFLICT。</summary>
        public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
        /// <summary>业务审计写入失败，不能发布未完成命令。</summary>
        public const string LogWriteFailed = "LOG_WRITE_FAILED";
        /// <summary>持久化写入未成功，详情只能留在服务端日志。</summary>
        public const string WriteFailed = "WRITE_FAILED";
        /// <summary>仓储新增未成功；可用于行级保存结果，不自动决定整个响应状态。</summary>
        public const string InsertFailed = "INSERT_FAILED";
        /// <summary>内部参与者缺少调用方事务，属于执行边界错误。</summary>
        public const string TransactionRequired = "TRANSACTION_REQUIRED";
        /// <summary>当前数据库结构尚不支持所请求的执行能力。</summary>
        public const string SchemaNotReady = "SCHEMA_NOT_READY";
        /// <summary>操作原因格式无效，保留既有 HTTP 登记。</summary>
        public const string InvalidReason = "INVALID_REASON";
        /// <summary>遗留命令身份被其他内容占用，保留原值。</summary>
        public const string CommandReused = "COMMAND_REUSED";
    }
}
