using System;
using System.Windows.Forms;
using SwiftExport.Client.VSTO.Hosting.Windows;
using SwiftExport.Client.VSTO.Hosting.TaskPanes;

namespace SwiftExport.Client.VSTO.Features.Sales.Orders.Views
{
    /// <summary>通过页面 Scope 的非模态窗体 Owner 显示销售确认。</summary>
    public sealed class SalesOrderImportPrompt : ISalesOrderImportPrompt
    {
        private readonly ModelessWindowContext context;
        private readonly TaskPaneContext paneContext;
        /// <summary>注入同 Scope 的窗体与侧栏上下文，由实际宿主提供确认框 Owner。</summary>
        public SalesOrderImportPrompt(ModelessWindowContext context, TaskPaneContext paneContext = null) { this.context = context; this.paneContext = paneContext; }
        /// <summary>客户切换、重新读取、关闭或重复订单确认归属销售窗体；宿主已释放时拒绝继续。</summary>
        public bool ConfirmDiscard(string message)
        {
            IWin32Window owner = context.Handle != IntPtr.Zero ? (IWin32Window)context : paneContext;
            return owner != null && owner.Handle != IntPtr.Zero && MessageBox.Show(owner, message, "SwiftExport", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }
    }
}
