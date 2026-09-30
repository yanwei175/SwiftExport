using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Client.VSTO.Diagnostics;
using System;
using System.Collections.Generic;
using System.Windows.Forms;
using DrawingSize = System.Drawing.Size;
using UIElement = System.Windows.UIElement;

namespace SwiftExport.Client.VSTO.Hosting.Windows
{
    /// <summary>在 Excel STA 显示由 ElementHost 承载的非模态 WinForms 窗体，并管理页面 Scope。</summary>
    public sealed class ModelessElementHostWindowManager : IDisposable
    {
        private readonly IServiceProvider provider;
        private readonly AddInLog log;
        private readonly Dictionary<string, WindowSession> windows = new Dictionary<string, WindowSession>();
        private bool disposed;

        /// <summary>借用插件服务容器；仅在实际创建页面时建立独立 Scope。</summary>
        public ModelessElementHostWindowManager(IServiceProvider provider, AddInLog log)
        { this.provider = provider; this.log = log; }

        /// <summary>按 Excel 窗口及页面键复用窗体；可在用户关闭时检查页面未保存内容，插件退出不阻塞。</summary>
        public void Show<TView>(string key, string title, IntPtr owner, DrawingSize size, Func<TView, bool> canClose = null) where TView : UIElement
        {
            if (disposed) throw new ObjectDisposedException(nameof(ModelessElementHostWindowManager));

            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("窗口页面键不能为空", nameof(key));

            var instanceKey = owner.ToInt64() + "_" + key;

            WindowSession existing;

            if (windows.TryGetValue(instanceKey, out existing))
            {
                if (existing.Form.WindowState == FormWindowState.Minimized) existing.Form.WindowState = FormWindowState.Normal;
                existing.Form.Activate();
                return;
            }

            IServiceScope scope = null;
            ModelessElementHostForm form = null;
            try
            {
                scope = provider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ModelessWindowContext>();

                context.Handle = owner;

                form = new ModelessElementHostForm(title, size, owner);
                form.LoadWpfUI(scope.ServiceProvider.GetRequiredService<TView>());
                context.Handle = form.Handle;
                var pageForm = form;
                form.FormClosing += (sender, args) =>
                {
                    if (disposed || args.CloseReason != CloseReason.UserClosing || canClose == null) return;
                    try
                    {
                        if (pageForm.Content is TView view && !canClose(view)) args.Cancel = true;
                    }
                    catch (Exception ex)
                    {
                        args.Cancel = true;
                        log.Write("ModelessWindowCloseCheckFailure", ex);
                    }
                };
                var session = new WindowSession(form, scope);
                form.FormClosed += (sender, args) => Remove(instanceKey, session);
                windows.Add(instanceKey, session);
                scope = null; form = null;
                if (owner == IntPtr.Zero) session.Form.Show();
                else session.Form.Show(new NativeOwner(owner));
            }
            catch
            {
                WindowSession failed;
                if (windows.TryGetValue(instanceKey, out failed)) Remove(instanceKey, failed);
                else
                {
                    try { form?.Dispose(); }
                    finally { scope?.Dispose(); }
                }
                throw;
            }
        }

        // 先断开 WPF 内容再释放 Scope；关闭与插件退出可能先后触发，按实例身份避免误删新窗体。
        private void Remove(string key, WindowSession session)
        {
            WindowSession current;
            if (!windows.TryGetValue(key, out current) || !ReferenceEquals(current, session)) return;
            windows.Remove(key);
            try { session.Dispose(); }
            catch (Exception ex) { log.Write("ModelessWindowCleanupFailure", ex); }
        }

        /// <summary>插件退出时关闭全部自有窗体，使其页面查询和事件在容器销毁前释放。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var pair in new List<KeyValuePair<string, WindowSession>>(windows))
            {
                try { pair.Value.Form.Close(); }
                catch (Exception ex) { log.Write("ModelessWindowCloseFailure", ex); }
                Remove(pair.Key, pair.Value);
            }
        }

        /// <summary>只借用 Excel 的 HWND，不持有 COM 对象。</summary>
        private sealed class NativeOwner : IWin32Window
        {
            public IntPtr Handle { get; }
            internal NativeOwner(IntPtr handle) { Handle = handle; }
        }

        /// <summary>将 WinForms 桥接器、窗体及 DI Scope 作为一个窗口会话释放。</summary>
        private sealed class WindowSession : IDisposable
        {
            private readonly IServiceScope scope;
            private bool disposed;
            internal ModelessElementHostForm Form { get; }
            internal WindowSession(ModelessElementHostForm form, IServiceScope scope)
            { Form = form; this.scope = scope; }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                try { Form.Dispose(); }
                finally { scope.Dispose(); }
            }
        }
    }
}
