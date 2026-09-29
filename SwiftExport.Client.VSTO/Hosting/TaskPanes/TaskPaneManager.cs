using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Office.Tools;
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Client.VSTO.Authentication;
using SwiftExport.Client.VSTO.Diagnostics;
using ExcelInterop = Microsoft.Office.Interop.Excel;
using Office = Microsoft.Office.Core;

namespace SwiftExport.Client.VSTO.Hosting.TaskPanes
{
    /// <summary>沿用 MateExcel 的 HWND＋页面标识索引及点击切换；不定时枚举 COM 窗口删除窗格。</summary>
    public sealed class TaskPaneManager : IDisposable
    {
        private readonly CustomTaskPaneCollection panes;
        private readonly AddInLog log;
        private readonly IServiceProvider provider;
        private readonly TaskPaneRegistrar registrar;
        private readonly SessionService authentication;
        private readonly Dictionary<string, TaskPaneSession> sessions = new Dictionary<string, TaskPaneSession>();
        private bool disposed;
        /// <summary>借用宿主窗格集合与 COM 身份，不主动释放共享 RCW。</summary>
        public TaskPaneManager(CustomTaskPaneCollection panes, AddInLog log,
            IServiceProvider provider, TaskPaneRegistrar registrar, SessionService authentication)
        {
            this.panes = panes; this.log = log;
            this.provider = provider; this.registrar = registrar; this.authentication = authentication;
        }

        /// <summary>按示例点击切换显示／隐藏；复用时不创建 Scope、不覆盖用户调整的尺寸。</summary>
        public void TogglePane(ExcelInterop.Window window, string key)
        {
            TaskPaneHostControl host = null;
            CustomTaskPane pane = null;
            IServiceScope scope = null;
            try
            {
                if (disposed) throw new ObjectDisposedException(nameof(TaskPaneManager));
                if (window == null || authentication.Capture().User == null) return;
                if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("窗格页面键不能为空", nameof(key));
                var config = registrar.Get(key);
                var owner = new IntPtr(window.Hwnd);
                var instanceKey = owner.ToInt64() + "_" + key;
                TaskPaneSession existing;
                if (sessions.TryGetValue(instanceKey, out existing))
                {
                    bool visible;
                    try { visible = existing.Pane.Visible; }
                    catch (ObjectDisposedException) { Remove(instanceKey, existing); visible = false; existing = null; }
                    catch (COMException ex) when (ex.ErrorCode == unchecked((int)0x80010108) || ex.ErrorCode == unchecked((int)0x800401FD))
                    {
                        // 只把已断开的 COM 对象视为失效；Excel 忙碌错误保留原页面并交给入口报告。
                        Remove(instanceKey, existing); visible = false; existing = null;
                    }
                    if (existing != null)
                    {
                        existing.Pane.Visible = !visible;
                        return;
                    }
                }
                scope = provider.CreateScope();
                scope.ServiceProvider.GetRequiredService<TaskPaneContext>().Handle = owner;
                host = new TaskPaneHostControl();
                host.LoadWpfUI((System.Windows.UIElement)scope.ServiceProvider.GetRequiredService(config.ViewType));
                pane = panes.Add(host, config.Title, window);
                pane.DockPosition = config.DockPosition == System.Windows.Controls.Dock.Left
                    ? Office.MsoCTPDockPosition.msoCTPDockPositionLeft
                    : Office.MsoCTPDockPosition.msoCTPDockPositionRight;
                // 留出工作表操作空间；创建后不再覆盖用户调整的宽度和停靠位置。
                pane.Width = config.DefaultWidth;
                pane.Visible = true;
                sessions.Add(instanceKey, new TaskPaneSession(pane, host, scope));
                host = null; pane = null; scope = null;
            }
            finally
            {
                if (pane != null) Cleanup(() => panes.Remove(pane));
                if (host != null) Cleanup(host.Dispose);
                if (scope != null) Cleanup(scope.Dispose);
            }
        }

        // 仅在再次访问确认失效后移出索引；不使用 RCW 引用比较推断 Excel 窗口已关闭。
        private void Remove(string instanceKey, TaskPaneSession session)
        {
            sessions.Remove(instanceKey);
            Cleanup(() => panes.Remove(session.Pane));
            Cleanup(session.Dispose);
        }
        private void Cleanup(Action action)
        { try { action(); } catch (Exception ex) { log.Write("TaskPaneCleanupFailure", ex); } }

        /// <summary>插件退出释放全部缓存页面；窗格集合最终销毁由 VSTO 负责。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var session in sessions.Values) Cleanup(session.Dispose);
            sessions.Clear();
        }
    }
    /// <summary>保存窗格和页面 Scope；隐藏不释放，失效重建或插件退出释放。</summary>
    internal sealed class TaskPaneSession : IDisposable
    {
        private readonly TaskPaneHostControl host;
        private readonly IDisposable scope;
        private bool disposed;
        internal CustomTaskPane Pane { get; }
        internal TaskPaneSession(CustomTaskPane pane, TaskPaneHostControl host, IDisposable scope)
        { Pane = pane; this.host = host; this.scope = scope; }
        /// <summary>即使宿主控件释放失败也释放 Scope，解绑页面事件并取消查询。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { host.Dispose(); }
            finally { scope?.Dispose(); }
        }
    }
}
