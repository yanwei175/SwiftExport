namespace SwiftExport.Contracts.Wrappers
{
    /// <summary>
    /// 封装方法执行结果，可统一返回成功/失败消息和数据
    /// </summary>
    public class ServiceResult<T> : IServiceResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T Data { get; set; }

        /// <summary>获取或设置结构化错误；成功时为空，失败时必须包含错误码。</summary>
        public SwiftExport.Contracts.DTOs.Errors.ServiceErrorDto Error { get; set; }

        /// <summary>创建成功结果，并保持原有数据与消息契约。</summary>
        public static ServiceResult<T> Ok(T data, string msg = "成功") => new ServiceResult<T> { Success = true, Data = data, Message = msg };

        /// <summary>创建业务规则阻断结果；其他类别必须显式提供错误码。</summary>
        public static ServiceResult<T> Fail(string msg) => Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.BusinessBlocked, msg);

        /// <summary>创建包含稳定错误代码和可选字段路径的结构化失败结果。</summary>
        public static ServiceResult<T> Fail(string code, string message, string field = null)
        {
            return new ServiceResult<T>
            {
                Success = false,
                Message = message,
                Error = new SwiftExport.Contracts.DTOs.Errors.ServiceErrorDto
                {
                    Code = code,
                    Message = message,
                    Field = field
                }
            };
        }
    }

}
