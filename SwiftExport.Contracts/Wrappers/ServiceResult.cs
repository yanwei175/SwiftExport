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
        // 快捷方法：成功
        public static ServiceResult<T> Ok(T data, string msg = "成功") => new ServiceResult<T> { Success = true, Data = data, Message = msg };
        // 快捷方法：失败
        public static ServiceResult<T> Fail(string msg) => new ServiceResult<T> { Success = false, Message = msg };
    }

}
