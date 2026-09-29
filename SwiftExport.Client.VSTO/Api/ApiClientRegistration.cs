using System;
using System.Collections.Generic;
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
        private readonly HttpMessageHandler authenticatedTransport;
        private readonly List<HttpClient> apiClients = new List<HttpClient>();
        private bool disposed;
        /// <summary>按契约路由创建长期代理，共享当前账号处理器；相对路径例如 api/Customer。</summary>
        public T Create<T>(string relativePath = null)
        {
            lock (apiClients)
            {
                if (disposed) throw new ObjectDisposedException(nameof(ApiClientRegistration));
                if (string.IsNullOrWhiteSpace(relativePath)) return RestService.For<T>(authenticated);
                var client = new HttpClient(authenticatedTransport, false)
                {
                    // Refit 自己拼接方法前导斜线，控制器前缀不能带末尾斜线。
                    BaseAddress = new Uri(authenticated.BaseAddress, relativePath.Trim('/')),
                    Timeout = authenticated.Timeout
                };
                apiClients.Add(client);
                return RestService.For<T>(client);
            }
        }
        /// <summary>设置受信服务器和明确超时，不改变证书验证。</summary>
        public ApiClientRegistration(Uri server, SessionService session)
            : this(server, session, new HttpClientHandler()) { }
        // 测试可替换传输，仍执行 Contracts 中真实生成的 Refit 代理。
        internal ApiClientRegistration(Uri server, SessionService session, HttpMessageHandler candidateTransport,
            HttpMessageHandler businessTransport = null)
        {
            this.candidateTransport = candidateTransport;
            candidates = new HttpClient(candidateTransport, false) { BaseAddress = server, Timeout = TimeSpan.FromSeconds(15) };
            authenticatedTransport = new AuthTokenHandler(session) { InnerHandler = businessTransport ?? new HttpClientHandler() };
            authenticated = new HttpClient(authenticatedTransport, false)
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
        public void Dispose()
        {
            lock (apiClients)
            {
                if (disposed) return;
                disposed = true;
                foreach (var client in apiClients) client.Dispose();
                apiClients.Clear();
                candidates.Dispose();
                authenticated.Dispose();
                candidateTransport.Dispose();
                authenticatedTransport.Dispose();
            }
        }
    }
}
