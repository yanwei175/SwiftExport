using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Client.VSTO.Hosting.Windows;
using System;

namespace SwiftExport.Client.VSTO.Authentication
{
    /// <summary>合并 Excel 登录入口，以模态窗口完成认证并在返回后释放页面 Scope。</summary>
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

        /// <summary>在 Excel STA 显示模态登录窗，取消或异常均释放 Scope 并复位入口状态。</summary>
        public void Show(IntPtr owner)
        {
            if (IsOpen) return;
            var scope = provider.CreateScope();
            IsOpen = true;
            try
            {
                Changed?.Invoke(this, EventArgs.Empty);
                windows.ShowDialog("login", owner,
                    () => scope.ServiceProvider.GetRequiredService<LoginWindow>());
            }
            finally
            {
                try { scope.Dispose(); }
                finally
                {
                    IsOpen = false;
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }
}
