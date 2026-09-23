using SwiftExport.Contracts.DTOs.Errors;

namespace SwiftExport.Contracts.Wrappers
{
    /// <summary>提供与数据类型无关的结果视图，供 HTTP 边界统一处理失败。</summary>
    public interface IServiceResult
    {
        /// <summary>业务操作是否成功。</summary>
        bool Success { get; }
        /// <summary>面向用户的操作提示。</summary>
        string Message { get; }
        /// <summary>失败的稳定代码、提示和字段路径。</summary>
        ServiceErrorDto Error { get; }
    }
}
