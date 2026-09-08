using System;
using SwiftExport.Contracts.DTOs.Auth;

namespace SwiftExport.Client.VSTO.Authentication
{
    /// <summary>保存一次请求的身份版本，防止旧请求影响新账号。</summary>
    public sealed class SessionSnapshot
    {
        public long Version { get; internal set; }
        public string Token { get; internal set; }
        public MeDto User { get; internal set; }
    }

    /// <summary>以锁保护内存会话；事件可能来自网络线程，由 UI 订阅方调度。</summary>
    public sealed class SessionService : IDisposable
    {
        private readonly object gate = new object();
        private readonly ITokenStore store;
        private long version;
        private string token;
        private MeDto user;
        private bool disposed;
        /// <summary>身份改变通知，不携带令牌。</summary>
        public event EventHandler Changed;
        /// <summary>使用当前服务器的持久化存储建立会话。</summary>
        public SessionService(ITokenStore store) { this.store = store; }
        /// <summary>取得不可回写的会话快照。</summary>
        public SessionSnapshot Capture()
        {
            lock (gate) return new SessionSnapshot { Version = version, Token = token, User = user };
        }
        /// <summary>只有预期版本仍有效时接纳已验证身份；磁盘失败不替换旧会话。</summary>
        public bool TryCommit(long expectedVersion, string candidate, MeDto identity)
        {
            if (string.IsNullOrWhiteSpace(candidate) || identity == null || string.IsNullOrWhiteSpace(identity.UserId)) throw new ArgumentException("身份不完整。");
            lock (gate)
            {
                if (disposed || expectedVersion != version) return false;
                store.Write(candidate);
                token = candidate;
                user = identity;
                version++;
            }
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }
        /// <summary>清除匹配版本的会话；迟到的 401 不会退出新账号。</summary>
        public bool TryEnd(long expectedVersion)
        {
            lock (gate)
            {
                if (disposed || expectedVersion != version) return false;
                store.Clear();
                token = null;
                user = null;
                version++;
            }
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }
        /// <summary>使所有未完成操作失效；正常退出 Excel 保留加密登录缓存。</summary>
        public void Dispose()
        {
            lock (gate) { disposed = true; version++; token = null; user = null; }
        }
    }
}
