using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Authentication;
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
        /// <summary>为插件连接使用 TLS 1.2 和明确超时，保留证书验证，不改变宿主全局协议。</summary>
        public ApiClientRegistration(Uri server, SessionService session)
            : this(server, session, CreateTransport()) { }

        /// <summary>隔离 Excel AppDomain 的旧 TLS 默认值，认证及业务请求共用同一传输策略。</summary>
        internal static HttpClientHandler CreateTransport()
        {
            // VSTO 宿主可能仍默认 SSL 3.0／TLS 1.0；只配置本插件连接，不能修改其他插件的网络环境。
            return new HttpClientHandler { SslProtocols = SslProtocols.Tls12 };
        }
        // 测试可替换传输，仍执行 Contracts 中真实生成的 Refit 代理。
        internal ApiClientRegistration(Uri server, SessionService session, HttpMessageHandler candidateTransport,
            HttpMessageHandler businessTransport = null)
        {
            this.candidateTransport = candidateTransport;
            candidates = new HttpClient(candidateTransport, false) { BaseAddress = server, Timeout = TimeSpan.FromSeconds(15) };
            authenticatedTransport = new AuthTokenHandler(session) { InnerHandler = businessTransport ?? CreateTransport() };
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
