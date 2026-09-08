using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Refit;

namespace SwiftExport.Client.VSTO.Authentication
{
    /// <summary>验证候选身份，提交前核对会话与页面操作生命期。</summary>
    public sealed class AuthService
    {
        private readonly IAuthGateway gateway;
        private readonly SessionService session;
        private readonly ITokenStore store;
        /// <summary>注入无 UI 的认证依赖。</summary>
        public AuthService(IAuthGateway gateway, SessionService session, ITokenStore store)
        { this.gateway = gateway; this.session = session; this.store = store; }

        /// <summary>新身份验证成功才替换旧账号；取消和失败保留原身份。</summary>
        public async Task<bool> LoginAsync(string name, string password, CancellationToken cancellationToken)
        {
            var before = session.Capture();
            var candidate = await gateway.LoginAsync(name.Trim(), password);
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(candidate)) throw new InvalidOperationException("服务器没有返回令牌。");
            var identity = await gateway.GetMeAsync(candidate);
            cancellationToken.ThrowIfCancellationRequested();
            return session.TryCommit(before.Version, candidate, identity);
        }

        /// <summary>验证缓存令牌；仅明确的 401 清除缓存，网络错误交由调用方提示。</summary>
        public async Task RestoreAsync(CancellationToken cancellationToken)
        {
            var before = session.Capture();
            var cached = store.Read();
            if (string.IsNullOrEmpty(cached)) return;
            try
            {
                var identity = await gateway.GetMeAsync(cached);
                cancellationToken.ThrowIfCancellationRequested();
                session.TryCommit(before.Version, cached, identity);
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                cancellationToken.ThrowIfCancellationRequested();
                session.TryEnd(before.Version);
            }
        }
    }
}
