using System;
using System.Windows.Forms;

namespace SwiftExport.Client.VSTO.Hosting.Windows
{
    /// <summary>保存非模态页面确认框的 WinForms Owner；随页面 Scope 清除句柄。</summary>
    public sealed class ModelessWindowContext : IWin32Window, IDisposable
    {
        /// <summary>当前窗体句柄；窗口创建前暂用所属 Excel 窗口句柄。</summary>
        public IntPtr Handle { get; internal set; }

        /// <summary>解除已关闭窗体的句柄引用。</summary>
        public void Dispose() { Handle = IntPtr.Zero; }
    }
}
