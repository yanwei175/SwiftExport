using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using SwiftExport.Client.VSTO.Authentication;

namespace SwiftExport.Client.VSTO.Api
{
    /// <summary>仅用于当前账号业务请求；候选登录不经过此处理器。</summary>
    public sealed class AuthTokenHandler : DelegatingHandler
    {
        private readonly SessionService session;
        /// <summary>绑定当前内存会话。</summary>
        public AuthTokenHandler(SessionService session) { this.session = session; }
        /// <summary>附加请求身份；丢弃账号切换后返回的响应，并按版本处理 401。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var before = session.Capture();
            if (!string.IsNullOrEmpty(before.Token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", before.Token);
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (session.Capture().Version != before.Version)
            {
                response.Dispose();
                throw new OperationCanceledException("账号已改变，忽略旧响应。");
            }
            if (response.StatusCode == HttpStatusCode.Unauthorized) session.TryEnd(before.Version);
            return response;
        }
    }
}
