namespace SwiftExport.Contracts.Wrappers
{
    /// <summary>
    /// 封装方法执行结果，可统一返回成功/失败消息和数据
    /// </summary>
    public class ServiceResult<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T Data { get; set; }

        /// <summary>获取或设置结构化错误；旧式失败结果可保持为空。</summary>
        public SwiftExport.Contracts.DTOs.Errors.ServiceErrorDto Error { get; set; }

        /// <summary>创建成功结果，并保持原有数据与消息契约。</summary>
        public static ServiceResult<T> Ok(T data, string msg = "成功") => new ServiceResult<T> { Success = true, Data = data, Message = msg };

        /// <summary>创建兼容旧调用方的纯消息失败结果。</summary>
        public static ServiceResult<T> Fail(string msg) => new ServiceResult<T> { Success = false, Message = msg };

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
