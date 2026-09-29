using System;
using System.Windows.Forms;

namespace SwiftExport.Client.VSTO.Hosting.TaskPanes
{
    /// <summary>每个窗格 Scope 独有的宿主上下文，不向页面模型暴露 Excel COM。</summary>
    public sealed class TaskPaneContext : IWin32Window, IDisposable
    {
        /// <summary>确认框所属窗口句柄，由管理器在创建页面前设置。</summary>
        public IntPtr Handle { get; internal set; }
        /// <summary>释放时解除窗口关联，防止使用失效句柄。</summary>
        public void Dispose() { Handle = IntPtr.Zero; }
    }
}
