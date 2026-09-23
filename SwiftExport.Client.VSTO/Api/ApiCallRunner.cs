using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
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
                // 只接纳标准失败正文中的用户提示，绝不回显网关 HTML 或原始响应。
                if (!string.IsNullOrWhiteSpace(api.Content))
                {
                    try
                    {
                        using (var document = JsonDocument.Parse(api.Content))
                        {
                            var root = document.RootElement;
                            JsonElement success, error, code, message;
                            if (root.ValueKind == JsonValueKind.Object
                                && root.TryGetProperty("success", out success) && success.ValueKind == JsonValueKind.False
                                && root.TryGetProperty("error", out error) && error.ValueKind == JsonValueKind.Object
                                && error.TryGetProperty("code", out code) && code.ValueKind == JsonValueKind.String
                                && !string.IsNullOrWhiteSpace(code.GetString())
                                && error.TryGetProperty("message", out message) && message.ValueKind == JsonValueKind.String
                                && !string.IsNullOrWhiteSpace(message.GetString()))
                                return message.GetString();
                        }
                    }
                    catch (JsonException) { /* 损坏或非 JSON 响应按 HTTP 状态给安全提示。 */ }
                }
                if (api.StatusCode == HttpStatusCode.Unauthorized) return "账号密码错误或登录已失效，请重新登录。";
                if (api.StatusCode == HttpStatusCode.Forbidden) return "当前账号没有访问权限。";
                return "服务器未能完成请求，请稍后重试。";
            }
            if (exception is HttpRequestException || exception is OperationCanceledException) return "无法连接服务器或请求超时，请检查网络后重试。";
            return "操作未完成，请重试或查看插件日志。";
        }
    }
}
