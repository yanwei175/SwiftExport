using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Client.VSTO.Authentication;
using SwiftExport.Client.VSTO.Diagnostics;
using SwiftExport.Client.VSTO.Excel.Context;
using SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.Views;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Views;
using SwiftExport.Client.VSTO.Hosting;
using SwiftExport.Client.VSTO.Hosting.TaskPanes;
using SwiftExport.Client.VSTO.Features.Settings.ExcelReadProfiles.Views;
using SwiftExport.Client.VSTO.Hosting.Threading;
using SwiftExport.Client.VSTO.Hosting.Windows;
using SwiftExport.Client.VSTO.Ioc;
using SwiftExport.Contracts.DTOs.Auth;
using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
        private ExcelContextService context;
        private TaskPaneManager taskPanes;
        private SessionService session;
        private CurrentUserService user;
        private LoginCoordinator login;
        private bool disposed;
        public bool IsRestoring { get; private set; }
        public bool IsLoginOpen { get { return login?.IsOpen == true; } }
        public bool IsAuthenticated { get { return user?.IsAuthenticated == true; } }
        /// <summary>根据已验证会话的内存权限控制地址配置入口，兼容已有授权键。</summary>
        public bool CanManageExcelReadProfiles { get { return user?.HasPermission(PermissionKeys.SystemExcelFieldMappingManage) == true; } }
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
                provider = ServiceRegistration.Build(ApiOptions.Load(directory), dispatcher, log, addIn.Application);
                session = provider.GetRequiredService<SessionService>();
                user = provider.GetRequiredService<CurrentUserService>();
                login = provider.GetRequiredService<LoginCoordinator>();
                login.Changed += Session_Changed;
                context = new ExcelContextService(addIn.Application);
                taskPanes = new TaskPaneManager(addIn.CustomTaskPanes, log, provider, provider.GetRequiredService<TaskPaneRegistrar>(), session);
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
            catch (Exception ex)
            {
                log.Write("OpenLoginFailure", ex);
                Status = "无法打开登录窗口，请查看日志";
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        /// <summary>清除插件身份，不修改工作簿。</summary>
        public void Logout()
        {
            if (!IsReady) return;
            try { session.TryEnd(session.Capture().Version); }
            catch (Exception ex)
            {
                log.Write("LogoutFailure", ex);
                Status = "退出未完成，请重试";
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        /// <summary>在触发操作的 Excel 窗口打开非模态销售导入；重复点击激活原窗体。</summary>
        public void OpenSalesOrderImport(Office.IRibbonControl control)
        {
            if (!IsReady || !IsAuthenticated || IsRestoring || IsLoginOpen) return;
            try
            {
                provider.GetRequiredService<ModelessElementHostWindowManager>()
                .Show<SalesOrderImportView>("sales-order-import", "销售订单导入", context.GetOwner(control),
                    new Size(1280, 820), view => view.CanClose(),
                    view => view.SetConfigurationOpener(uid => OpenExcelReadProfile(null, uid)));
            }
            catch (Exception ex)
            {
                log.Write("OpenSalesImportFailure", ex);
                Status = "无法打开销售导入窗口，请查看日志";
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        /// <summary>打开非模态采购导入，重复点击激活原窗体。</summary>
        public void OpenPurchaseOrderImportPreview(Office.IRibbonControl control)
        {
            if (!IsReady || !IsAuthenticated || IsRestoring || IsLoginOpen) return;
            try
            {
                provider.GetRequiredService<ModelessElementHostWindowManager>()
                .Show<PurchaseOrderImportView>("purchase-order-import-preview", "采购订单导入", context.GetOwner(control), new Size(1280, 820),
                    view => view.CanClose(), view => view.SetConfigurationOpener(uid => OpenExcelReadProfile(null, uid, "PurchaseOrder")));
            }
            catch (Exception ex)
            {
                log.Write("OpenPurchaseImportPreviewFailure", ex);
                Status = "无法打开采购导入，请查看日志";
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        /// <summary>创建装箱单导入窗口，同 Scope 释放页面，后续来源关联留在 .NET 10。</summary>
        public void OpenPackingListImport(Office.IRibbonControl control)
        {
            if (!IsReady || !IsAuthenticated || IsRestoring || IsLoginOpen || user?.HasPermission("Shipping.PackingList.Create") != true) return;
            try
            {
                provider.GetRequiredService<ModelessElementHostWindowManager>().Show<SwiftExport.Client.VSTO.Features.Shipping.PackingLists.Views.PackingListImportView>(
                    "packing-list-import", "PackingList 导入", context.GetOwner(control), new Size(1280, 820), view => view.CanClose(),
                    view => view.SetConfigurationOpener(uid => OpenExcelReadProfile(null, uid, "PackingList")));
            }
            catch (Exception ex) { log.Write("OpenPackingListImportFailure", ex); Status = "无法打开装箱单导入，请查看日志"; Changed?.Invoke(this, EventArgs.Empty); }
        }
        /// <summary>Ribbon 只读内存中的装箱单创建权限。</summary>
        public bool CanImportPackingList => user?.HasPermission("Shipping.PackingList.Create") == true;
        /// <summary>Ribbon 切换配置侧栏，导入页按业务组受控定位配置。</summary>
        public void OpenExcelReadProfile(Office.IRibbonControl control, Guid? initialProfile = null, string groupCode = "SalesOrder")
        {
            if (!IsReady || !CanManageExcelReadProfiles || IsRestoring || IsLoginOpen) return;
            try
            {
                var window = context.GetWindow(control);
                if (window == null) return;
                if (control != null) taskPanes.TogglePane(window, "excel-read-profile");
                else taskPanes.ShowPane(window, "excel-read-profile", view => ((ExcelReadProfileView)view).OpenGroup(groupCode, initialProfile));
            }
            catch (Exception ex)
            {
                log.Write("OpenReadProfileFailure", ex);
                Status = "无法打开读取配置侧栏，请查看日志";
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        /// <summary>逐项尽力清理，不结束宿主进程和消息循环。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            if (session != null) { session.Changed -= Session_Changed; session.Dispose(); }
            if (login != null) login.Changed -= Session_Changed;
            Cleanup(() => provider?.GetRequiredService<ModelessElementHostWindowManager>().Dispose());
            Cleanup(() => provider?.GetRequiredService<WindowManager>().Dispose());
            Cleanup(() => taskPanes?.Dispose());
            Cleanup(() => provider?.Dispose());
            Cleanup(() => wpf?.Dispose());
            Cleanup(() => dispatcher?.Dispose());
            log.Write("Stopped");
        }
        private void Cleanup(Action action) { try { action(); } catch (Exception ex) { log.Write("CleanupFailure", ex); } }
    }
}
