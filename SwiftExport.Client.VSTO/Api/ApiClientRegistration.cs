using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Client.VSTO.Authentication;

namespace SwiftExport.Client.VSTO.Api
{
    /// <summary>持有长期 HTTP 连接，候选身份使用每次调用独立授权头。</summary>
    public sealed class ApiClientRegistration : IAuthGateway, IDisposable
    {
        private readonly HttpClient candidates;
        private readonly HttpMessageHandler candidateTransport;
        private readonly HttpClient authenticated;
        /// <summary>当前账号 API 创建入口；未来业务接口从此取得。</summary>
        public T Create<T>() { return RestService.For<T>(authenticated); }
        /// <summary>设置受信服务器和明确超时，不改变证书验证。</summary>
        public ApiClientRegistration(Uri server, SessionService session)
            : this(server, session, new HttpClientHandler()) { }
        // 测试可替换传输，仍执行 Contracts 中真实生成的 Refit 代理。
        internal ApiClientRegistration(Uri server, SessionService session, HttpMessageHandler candidateTransport)
        {
            this.candidateTransport = candidateTransport;
            candidates = new HttpClient(candidateTransport, false) { BaseAddress = server, Timeout = TimeSpan.FromSeconds(15) };
            authenticated = new HttpClient(new AuthTokenHandler(session) { InnerHandler = new HttpClientHandler() })
            { BaseAddress = server, Timeout = TimeSpan.FromSeconds(30) };
        }
        /// <inheritdoc />
        public async Task<string> LoginAsync(string userName, string password)
        {
            var result = await RestService.For<IAuthApi>(candidates).LoginAsync(new SwiftExport.Contracts.DTOs.Auth.LoginRequestDto { UserName = userName, Password = password });
            return result.Access_token;
        }
        /// <inheritdoc />
        public async Task<SwiftExport.Contracts.DTOs.Auth.MeDto> GetMeAsync(string token)
        {
            // 短期 facade 不拥有长期连接；避免修改共享 DefaultRequestHeaders。
            using (var client = new HttpClient(candidateTransport, false) { BaseAddress = candidates.BaseAddress, Timeout = candidates.Timeout })
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return await RestService.For<IAuthApi>(client).GetMeAsync();
            }
        }
        /// <summary>停止插件时中止自有连接上的未完成请求。</summary>
        public void Dispose() { candidates.Dispose(); authenticated.Dispose(); candidateTransport.Dispose(); }
    }
}
