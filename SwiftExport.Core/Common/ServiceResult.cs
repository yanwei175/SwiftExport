using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Common
{
    /// <summary>
    /// 封装方法执行结果，可统一返回成功/失败消息和数据
    /// </summary>
    public class ServiceResult<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T Data { get; set; }
        public static ServiceResult<T> Ok(T data, string msg = "成功") => new ServiceResult<T> { Success = true, Data = data, Message = msg };
        public static ServiceResult<T> Fail(string msg) => new ServiceResult<T> { Success = false, Message = msg };
    }

}
