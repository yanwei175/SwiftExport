using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Client.VSTO.Api;
using SwiftExport.Client.VSTO.Hosting.Threading;
using SwiftExport.Client.VSTO.Mvvm;

namespace SwiftExport.Client.VSTO.Authentication
{
    /// <summary>登录页状态与命令；密码由视图短期传入，不保存为绑定属性。</summary>
    public sealed class LoginViewModel : ViewModelBaseEx
    {
        private readonly AuthService auth;
        private readonly ApiCallRunner errors;
        private readonly IUiDispatcher ui;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private string userName = string.Empty;
        public string UserName { get { return userName; } set { if (SetProperty(ref userName, value)) RefreshCommands(); } }
        public IAsyncRelayCommand<string> LoginCommand { get; }
        /// <summary>成功后请求宿主关闭窗口。</summary>
        public event EventHandler Succeeded;
        /// <summary>注入认证与 UI 边界，页面不操作窗口。</summary>
        public LoginViewModel(AuthService auth, ApiCallRunner errors, IUiDispatcher ui)
        {
            this.auth = auth; this.errors = errors; this.ui = ui;
            Title = "登录 SwiftExport";
            LoginCommand = new AsyncRelayCommand<string>(LoginAsync, password => !IsDisposed && !IsBusy && !string.IsNullOrWhiteSpace(UserName));
            RegisterCommand(LoginCommand);
        }
        // 业务入口也检查 Busy，避免直接 ExecuteAsync 绕过按钮状态。
        private async Task LoginAsync(string password)
        {
            if (IsDisposed || IsBusy) return;
            if (string.IsNullOrEmpty(password)) { ErrorMessage = "请输入密码。"; return; }
            IsBusy = true;
            ErrorMessage = string.Empty;
            try
            {
                var success = await auth.LoginAsync(UserName, password, lifetime.Token);
                await ui.InvokeAsync(() => { if (!IsDisposed && success) Succeeded?.Invoke(this, EventArgs.Empty); });
            }
            catch (Exception ex)
            {
                if (!lifetime.IsCancellationRequested)
                    await ui.InvokeAsync(() => { if (!IsDisposed) ErrorMessage = errors.Describe(ex); });
            }
            finally
            {
                if (!IsDisposed) await ui.InvokeAsync(() => { if (!IsDisposed) IsBusy = false; });
            }
        }
        /// <summary>使迟到的候选验证失效。</summary>
        protected override void DisposeCore() { lifetime.Cancel(); }
    }
}
