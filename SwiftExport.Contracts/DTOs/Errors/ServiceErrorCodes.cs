namespace SwiftExport.Contracts.DTOs.Errors
{
    /// <summary>
    /// 定义跨业务模块稳定复用的服务错误代码；模块专属错误应保留在各自契约边界。
    /// </summary>
    public static class ServiceErrorCodes
    {
        public const string Unauthenticated = "UNAUTHENTICATED";
        public const string Forbidden = "FORBIDDEN";
        public const string Validation = "VALIDATION";
        public const string VersionConflict = "VERSION_CONFLICT";
        public const string CommandConflict = "COMMAND_CONFLICT";
        public const string BusinessBlocked = "BUSINESS_BLOCKED";
        public const string Unexpected = "UNEXPECTED";
    }
}
