using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Client.VSTO.Hosting.Windows;
using System;

namespace SwiftExport.Client.VSTO.Authentication
{
    /// <summary>合并所有 Excel 窗口的登录入口，窗口关闭时释放页面 Scope。</summary>
    public sealed class LoginCoordinator
    {
        private readonly IServiceProvider provider;
        private readonly WindowManager windows;
        public bool IsOpen { get; private set; }
        /// <summary>登录窗打开和关闭时通知 Ribbon 更新入口状态。</summary>
        public event EventHandler Changed;
        /// <summary>绑定唯一窗口管理器和组合容器。</summary>
        public LoginCoordinator(IServiceProvider provider, WindowManager windows)
        {
            this.provider = provider;
            this.windows = windows;
        }

        /// <summary>在 Excel STA 打开或激活登录窗。</summary>
        public void Show(IntPtr owner)
        {
            if (IsOpen) return;
            var scope = provider.CreateScope();
            IsOpen = true;
            Changed?.Invoke(this, EventArgs.Empty);
            try
            {
                windows.Show("login", owner, () =>
                {
                    var window = scope.ServiceProvider.GetRequiredService<LoginWindow>();
                    window.Closed += (sender, args) =>
                    {
                        IsOpen = false;
                        Changed?.Invoke(this, EventArgs.Empty);
                    };
                    return window;
                }, scope);
            }
            catch
            {
                IsOpen = false;
                Changed?.Invoke(this, EventArgs.Empty);
                throw;
            }
        }
    }
}
