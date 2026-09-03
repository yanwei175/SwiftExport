namespace SwiftExport.Contracts.DTOs.Errors
{
    /// <summary>
    /// 表示跨模块服务失败的结构化详情，供客户端按稳定代码和可选字段路径处理错误。
    /// </summary>
    public class ServiceErrorDto
    {
        /// <summary>获取或设置稳定的机器可读错误代码。</summary>
        public string Code { get; set; }

        /// <summary>获取或设置面向用户或日志的错误消息。</summary>
        public string Message { get; set; }

        /// <summary>获取或设置相关请求字段路径；非字段级错误时为空。</summary>
        public string Field { get; set; }
    }
}
