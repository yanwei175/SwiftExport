using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Client.VSTO.Authentication;
using SwiftExport.Client.VSTO.Diagnostics;
using SwiftExport.Client.VSTO.Excel.Context;
using SwiftExport.Client.VSTO.Hosting;
using SwiftExport.Client.VSTO.Hosting.TaskPanes;
using SwiftExport.Client.VSTO.Hosting.Threading;
using SwiftExport.Client.VSTO.Hosting.Windows;
using SwiftExport.Client.VSTO.Ioc;
using Office = Microsoft.Office.Core;

namespace SwiftExport.Client.VSTO.Bootstrapper
{
    /// <summary>组合插件资源并维护 Ribbon 可读取的 UI 状态。</summary>
    public sealed class AddInBootstrapper : IDisposable
    {
        private readonly AddInLog log = new AddInLog();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private ServiceProvider provider;
        private ExcelDispatcher dispatcher;
        private WpfRuntimeHost wpf;
        private TaskPaneManager taskPanes;
        private ExcelContextService context;
        private SessionService session;
        private CurrentUserService user;
        private LoginCoordinator login;
        private bool disposed;
        public bool IsRestoring { get; private set; }
        public bool IsLoginOpen { get { return login?.IsOpen == true; } }
        public bool IsAuthenticated { get { return user?.IsAuthenticated == true; } }
        public bool IsReady { get { return provider != null && !disposed; } }
        public string Status { get; private set; } = "未登录";
        /// <summary>只在 Excel STA 发布，Ribbon 据此刷新动态控件。</summary>
        public event EventHandler Changed;
        /// <summary>建立宿主服务；网络验证排队执行，不阻塞 Startup。</summary>
        public void Start(ThisAddIn addIn)
        {
            if (provider != null || disposed) return;
            try
            {
                dispatcher = new ExcelDispatcher();
                wpf = new WpfRuntimeHost();
                wpf.Initialize();
                var directory = Path.GetDirectoryName(new Uri(typeof(AddInBootstrapper).Assembly.CodeBase).LocalPath);
                provider = ServiceRegistration.Build(ApiOptions.Load(directory), dispatcher, log);
                session = provider.GetRequiredService<SessionService>();
                user = provider.GetRequiredService<CurrentUserService>();
                login = provider.GetRequiredService<LoginCoordinator>();
                login.Changed += Session_Changed;
                context = new ExcelContextService(addIn.Application);
                taskPanes = new TaskPaneManager(addIn.CustomTaskPanes, log,
                    provider, provider.GetRequiredService<TaskPaneRegistrar>(), session);
                session.Changed += Session_Changed;
                IsRestoring = true;
                Status = "正在验证登录状态";
                Changed?.Invoke(this, EventArgs.Empty);
                RestoreSafelyAsync();
                log.Write("Started");
            }
            catch (Exception ex)
            {
                log.Write("StartupFailure", ex);
                Dispose();
                Status = "插件初始化失败，请检查配置与日志";
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        // 异步边界观察全部异常，缓存恢复不占用 Startup 调用栈。
        private async void RestoreSafelyAsync()
        {
            try
            {
                Task restore = null;
                await dispatcher.InvokeAsync(() => restore = provider.GetRequiredService<AuthService>().RestoreAsync(lifetime.Token));
                await restore;
                await dispatcher.InvokeAsync(() => { if (!disposed) { IsRestoring = false; RefreshStatus(); } });
            }
            catch (Exception ex)
            {
                if (disposed) return;
                log.Write("RestoreFailure", ex);
                try { await dispatcher.InvokeAsync(() => { if (!disposed) { IsRestoring = false; Status = "未登录（验证未完成，请点击登录）"; Changed?.Invoke(this, EventArgs.Empty); } }); }
                catch (OperationCanceledException) { }
            }
        }
        private async void Session_Changed(object sender, EventArgs args)
        {
            try { await dispatcher.InvokeAsync(() => { if (!disposed) RefreshStatus(); }); }
            catch (OperationCanceledException) { }
        }
        private void RefreshStatus() { Status = IsAuthenticated ? "当前用户：" + user.DisplayName : "未登录"; Changed?.Invoke(this, EventArgs.Empty); }
        /// <summary>处理 Ribbon 登录或切换入口。</summary>
        public void Login(Office.IRibbonControl control)
        {
            if (!IsReady || IsRestoring) return;
            try { provider.GetRequiredService<LoginCoordinator>().Show(context.GetOwner(control)); }
            catch (Exception ex) { log.Write("OpenLoginFailure", ex); Status = "无法打开登录窗口，请查看日志"; Changed?.Invoke(this, EventArgs.Empty); }
        }
        /// <summary>清除插件身份，不修改工作簿。</summary>
        public void Logout()
        {
            if (!IsReady) return;
            try { session.TryEnd(session.Capture().Version); }
            catch (Exception ex) { log.Write("LogoutFailure", ex); Status = "退出未完成，请重试"; Changed?.Invoke(this, EventArgs.Empty); }
        }
        /// <summary>在触发操作的 Excel 窗口下打开本地销售导入预览。</summary>
        public void OpenSalesOrderImport(Office.IRibbonControl control)
        {
            if (!IsReady || !IsAuthenticated || IsRestoring || IsLoginOpen) return;
            try { taskPanes.TogglePane(context.GetWindow(control), TaskPaneRegistrar.SalesOrderImport); }
            catch (Exception ex) { log.Write("OpenSalesImportFailure", ex); Status = "无法打开销售导入窗格，请查看日志"; Changed?.Invoke(this, EventArgs.Empty); }
        }
        /// <summary>在触发操作的 Excel 窗口显示采购导入静态布局预览。</summary>
        public void OpenPurchaseOrderImportPreview(Office.IRibbonControl control)
        {
            if (!IsReady || !IsAuthenticated || IsRestoring || IsLoginOpen) return;
            try { taskPanes.TogglePane(context.GetWindow(control), TaskPaneRegistrar.PurchaseOrderImport); }
            catch (Exception ex) { log.Write("OpenPurchaseImportPreviewFailure", ex); Status = "无法打开采购导入预览，请查看日志"; Changed?.Invoke(this, EventArgs.Empty); }
        }
        /// <summary>逐项尽力清理，不结束宿主进程和消息循环。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            if (session != null) { session.Changed -= Session_Changed; session.Dispose(); }
            if (login != null) login.Changed -= Session_Changed;
            Cleanup(() => taskPanes?.Dispose());
            Cleanup(() => provider?.GetRequiredService<WindowManager>().Dispose());
            Cleanup(() => provider?.Dispose());
            Cleanup(() => wpf?.Dispose());
            Cleanup(() => dispatcher?.Dispose());
            log.Write("Stopped");
        }
        private void Cleanup(Action action) { try { action(); } catch (Exception ex) { log.Write("CleanupFailure", ex); } }
    }
}
