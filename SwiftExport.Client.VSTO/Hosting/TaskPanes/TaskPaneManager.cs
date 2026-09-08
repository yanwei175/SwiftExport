using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Microsoft.Office.Tools;
using ExcelInterop = Microsoft.Office.Interop.Excel;

namespace SwiftExport.Client.VSTO.Hosting.TaskPanes
{
    /// <summary>按 Excel 窗口创建独立窗格，定时核对已关闭窗口以处理关闭取消和句柄失效。</summary>
    public sealed class TaskPaneManager : IDisposable
    {
        private readonly CustomTaskPaneCollection panes;
        private readonly ExcelInterop.Application application;
        private readonly List<TaskPaneSession> sessions = new List<TaskPaneSession>();
        private readonly Timer timer = new Timer { Interval = 1500 };
        /// <summary>借用宿主窗格集合；关闭期间不移除已被 VSTO 自动释放的窗格。</summary>
        public TaskPaneManager(CustomTaskPaneCollection panes, ExcelInterop.Application application)
        { this.panes = panes; this.application = application; timer.Tick += Reconcile; }
        /// <summary>为指定窗口添加窗格，资源所有者随窗格会话释放。</summary>
        public void Show(ExcelInterop.Window window, string title, System.Windows.UIElement content, IDisposable scope)
        {
            var host = new TaskPaneHostControl(content);
            CustomTaskPane pane = null;
            try
            {
                pane = panes.Add(host, title, window);
                pane.Visible = true;
                sessions.Add(new TaskPaneSession(pane, host, scope, window));
                timer.Start();
            }
            catch { if (pane != null) panes.Remove(pane); host.Dispose(); scope?.Dispose(); throw; }
        }
        // BeforeClose 可以取消，因此只在实际窗口集合变化后释放；Excel 忙时下轮再核对。
        private void Reconcile(object sender, EventArgs args)
        {
            try
            {
                var live = new List<ExcelInterop.Window>();
                foreach (ExcelInterop.Window window in application.Windows) live.Add(window);
                for (var i = sessions.Count - 1; i >= 0; i--)
                {
                    var session = sessions[i];
                    if (live.Contains(session.Window)) continue;
                    try { panes.Remove(session.Pane); }
                    catch (System.Runtime.InteropServices.COMException) { }
                    catch (ObjectDisposedException) { }
                    finally { session.Dispose(); sessions.RemoveAt(i); }
                }
                if (sessions.Count == 0) timer.Stop();
            }
            catch (System.Runtime.InteropServices.COMException) { }
        }
        /// <summary>停止观察并释放自有资源，集合最终销毁由 VSTO 负责。</summary>
        public void Dispose()
        {
            timer.Stop(); timer.Tick -= Reconcile; timer.Dispose();
            foreach (var session in sessions) session.Dispose();
            sessions.Clear();
        }
    }
    /// <summary>保持窗口 COM 身份而非只用可复用 HWND，窗口结束后释放引用。</summary>
    internal sealed class TaskPaneSession : IDisposable
    {
        private readonly TaskPaneHostControl host;
        private readonly IDisposable scope;
        private bool disposed;
        internal CustomTaskPane Pane { get; }
        internal ExcelInterop.Window Window { get; private set; }
        internal TaskPaneSession(CustomTaskPane pane, TaskPaneHostControl host, IDisposable scope, ExcelInterop.Window window)
        { Pane = pane; this.host = host; this.scope = scope; Window = window; }
        /// <summary>幂等断开 UI 和作用域，不强制释放共享 COM RCW。</summary>
        public void Dispose() { if (disposed) return; disposed = true; host.Dispose(); scope?.Dispose(); Window = null; }
    }
}
