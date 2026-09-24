namespace SwiftExport.Contracts.DTOs.Inventory.Outbounds
{
    /// <summary>出库命令专属错误码；普通校验、权限和版本冲突复用公共错误码。</summary>
    public static class OutboundErrorCodes
    {
        /// <summary>命令可能仍在执行，必须保留原请求重试。</summary>
        public const string CommandBusy = "OUTBOUND_COMMAND_BUSY";
        /// <summary>无法确认提交结果，不能重新生成命令标识。</summary>
        public const string OutcomeUnknown = "OUTBOUND_OUTCOME_UNKNOWN";
    }
}
