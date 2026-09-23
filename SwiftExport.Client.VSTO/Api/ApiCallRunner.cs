using System;
using System.Net;
using System.Net.Http;
using Refit;
using SwiftExport.Client.VSTO.Diagnostics;

namespace SwiftExport.Client.VSTO.Api
{
    /// <summary>将技术错误归一为不含服务端敏感正文的用户提示。</summary>
    public sealed class ApiCallRunner
    {
        private readonly AddInLog log;
        /// <summary>注入脱敏日志入口。</summary>
        public ApiCallRunner(AddInLog log) { this.log = log; }
        /// <summary>记录一次错误分类并返回中文提示。</summary>
        public string Describe(Exception exception)
        {
            log.Write("ApiFailure", exception);
            var api = exception as ApiException;
            if (api != null)
            {
                if (api.StatusCode == HttpStatusCode.Unauthorized) return "账号密码错误或登录已失效，请重新登录。";
                if (api.StatusCode == HttpStatusCode.Forbidden) return "当前账号没有访问权限。";
                return "服务器未能完成请求，请稍后重试。";
            }
            if (exception is HttpRequestException || exception is OperationCanceledException) return "无法连接服务器或请求超时，请检查网络后重试。";
            return "操作未完成，请重试或查看插件日志。";
        }
    }
}
