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
        /// <summary>在所属 Excel 窗口上显示模态对话框；返回确认结果，Scope 由调用方 finally 释放。</summary>
        public bool? ShowDialog(string key, IntPtr owner, Func<Window> factory)
        {
            Window existing;
            if (windows.TryGetValue(key, out existing))
            {
                existing.Activate();
                return null;
            }

            Window window = null;
            try
            {
                window = factory();
                if (owner != IntPtr.Zero) new WindowInteropHelper(window).Owner = owner;
                window.ShowInTaskbar = false;
                windows.Add(key, window);
                // 模态窗口使用 WPF 自有输入循环，不注册非模态键盘互操作。
                return window.ShowDialog();
            }
            finally
            {
                windows.Remove(key);
                // 显示失败或以 Hide 结束时也关闭窗口，正常关闭后无需重复操作。
                if (window != null && window.IsLoaded) window.Close();
            }
        }

        /// <summary>关闭自有窗口；非模态 Scope 由 Closed 释放，模态 Scope 由调用方 finally 释放。</summary>
        public void Dispose() { foreach (var window in new List<Window>(windows.Values)) window.Close(); windows.Clear(); }
    }
}
