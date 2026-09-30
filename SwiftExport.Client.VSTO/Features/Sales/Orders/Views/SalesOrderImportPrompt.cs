using System;
using System.Windows.Forms;
using SwiftExport.Client.VSTO.Hosting.Windows;

namespace SwiftExport.Client.VSTO.Features.Sales.Orders.Views
{
    /// <summary>通过页面 Scope 的非模态窗体 Owner 显示销售确认。</summary>
    public sealed class SalesOrderImportPrompt : ISalesOrderImportPrompt
    {
        private readonly ModelessWindowContext context;
        /// <summary>注入与当前页面同 Scope 的 WinForms 窗体上下文。</summary>
        public SalesOrderImportPrompt(ModelessWindowContext context) { this.context = context; }
        /// <summary>客户切换或关闭确认归属销售窗体；宿主已释放时拒绝丢弃。</summary>
        public bool ConfirmDiscard(string message) => context.Handle != IntPtr.Zero &&
            MessageBox.Show(context, message, "销售订单导入", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }
}
