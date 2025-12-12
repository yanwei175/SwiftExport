using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Common
{
    /// <summary>
    /// 封装方法执行结果，可统一返回成功/失败消息和数据
    /// </summary>
    public class Result<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public T Data { get; set; }

        public static Result<T> Ok(T data) => new Result<T> { Success = true, Data = data };
        public static Result<T> Fail(string msg) => new Result<T> { Success = false, Message = msg };
    }

}
