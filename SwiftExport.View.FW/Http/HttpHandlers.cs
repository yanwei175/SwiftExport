using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.Http
{
    /// <summary>
    /// HTTP 请求日志拦截器。
    /// 在 Debug 输出窗口打印每次请求的完整路径和响应状态码，便于接口联调排查。
    /// 生产环境可通过不挂载此 Handler 来关闭。
    /// </summary>
    public class HttpLoggingHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // 请求发出前：打印方法 + 绝对路径
            Debug.WriteLine("=================================================");
            Debug.WriteLine($"[HTTP 发送请求] {request.Method} {request.RequestUri}");
            Debug.WriteLine("=================================================");

            var response = await base.SendAsync(request, cancellationToken);

            // 响应回来后：打印状态码
            Debug.WriteLine($"[HTTP 收到响应] 状态码: {(int)response.StatusCode} ({response.StatusCode})");

            // 需要查看错误响应体时，取消下方注释：
            // if (!response.IsSuccessStatusCode && response.Content != null)
            // {
            //     var body = await response.Content.ReadAsStringAsync();
            //     Debug.WriteLine($"[HTTP 错误详情] {body}");
            // }

            return response;
        }
    }

    /// <summary>
    /// Bearer Token 注入拦截器（当前预留，实际启用时取消注释并实现 Token 获取逻辑）。
    /// 挂载方式：在 RefitExtensions.AddErpApi 中 .AddHttpMessageHandler&lt;AuthTokenHandler&gt;()
    /// </summary>
    public class AuthTokenHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // TODO：替换为从全局缓存或安全存储中获取最新 Token
            string latestToken = string.Empty;

            if (!string.IsNullOrEmpty(latestToken))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", latestToken);
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
