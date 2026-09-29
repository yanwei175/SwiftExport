using System;
using System.Windows.Forms;
using SwiftExport.Client.VSTO.Hosting.TaskPanes;

namespace SwiftExport.Client.VSTO.Features.Sales.Orders.Views
{
    /// <summary>通过页面 Scope 的通用宿主上下文显示销售确认，不需要专用 Coordinator 接线。</summary>
    public sealed class SalesOrderImportPrompt : ISalesOrderImportPrompt
    {
        private readonly TaskPaneContext context;
        /// <summary>注入与当前页面同 Scope 的宿主上下文。</summary>
        public SalesOrderImportPrompt(TaskPaneContext context) { this.context = context; }
        /// <summary>客户切换确认归属 Excel；宿主已释放时拒绝丢弃。</summary>
        public bool ConfirmDiscard(string message) => context.Handle != IntPtr.Zero &&
            MessageBox.Show(context, message, "销售订单导入", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }
}
