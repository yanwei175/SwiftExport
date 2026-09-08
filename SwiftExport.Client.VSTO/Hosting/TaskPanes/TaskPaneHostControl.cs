using System.Windows.Forms;
using System.Windows.Forms.Integration;

namespace SwiftExport.Client.VSTO.Hosting.TaskPanes
{
    /// <summary>将 WPF 内容托管进 VSTO 所需的 WinForms 控件。</summary>
    public sealed class TaskPaneHostControl : UserControl
    {
        private readonly ElementHost host;
        /// <summary>内容由会话创建；释放宿主时断开可视树。</summary>
        public TaskPaneHostControl(System.Windows.UIElement content)
        {
            host = new ElementHost { Dock = DockStyle.Fill, Child = content };
            Controls.Add(host);
        }
        /// <summary>断开 WPF 内容并释放 WinForms 子控件。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing) host.Child = null;
            base.Dispose(disposing);
        }
    }
}
