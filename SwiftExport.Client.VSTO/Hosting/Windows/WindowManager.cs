using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Forms.Integration;
using System.Windows.Interop;

namespace SwiftExport.Client.VSTO.Hosting.Windows
{
    /// <summary>在 Excel STA 管理唯一窗口及其资源所有者。</summary>
    public sealed class WindowManager : IDisposable
    {
        private readonly Dictionary<string, Window> windows = new Dictionary<string, Window>();
        /// <summary>已存在则激活；新建失败和关闭时均释放对应 Scope。</summary>
        public void Show(string key, IntPtr owner, Func<Window> factory, IDisposable scope)
        {
            Window existing;
            if (windows.TryGetValue(key, out existing))
            {
                scope?.Dispose();
                if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
                existing.Activate();
                return;
            }
            Window window = null;
            try
            {
                window = factory();
                if (owner != IntPtr.Zero) new WindowInteropHelper(window).Owner = owner;
                window.ShowInTaskbar = false;
                window.Closed += (sender, args) => { windows.Remove(key); scope?.Dispose(); };
                windows.Add(key, window);
                ElementHost.EnableModelessKeyboardInterop(window);
                window.Show();
            }
            catch { windows.Remove(key); window?.Close(); scope?.Dispose(); throw; }
        }
        /// <summary>关闭自有窗口，由 Closed 完成会话释放。</summary>
        public void Dispose() { foreach (var window in new List<Window>(windows.Values)) window.Close(); windows.Clear(); }
    }
}
