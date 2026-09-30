using System.Windows.Forms;
using System.Windows.Forms.Integration;

namespace SwiftExport.Client.VSTO.Hosting.TaskPanes
{
    /// <summary>将 WPF 内容托管进 VSTO 所需的 WinForms 控件。</summary>
    public sealed class TaskPaneHostControl : UserControl
    {
        private readonly ElementHost host;
        private bool contentDetached;
        /// <summary>先创建并挂接 ElementHost，与 MateExcel 示例保持相同的桥接顺序。</summary>
        public TaskPaneHostControl()
        {
            host = new ElementHost { Dock = DockStyle.Fill };
            Controls.Add(host);
        }
        /// <summary>供独立宿主测试使用的内容初始化入口。</summary>
        public TaskPaneHostControl(System.Windows.UIElement content) : this()
        {
            try { LoadWpfUI(content); }
            catch { Dispose(); throw; }
        }
        /// <summary>宿主创建完成后装载页面，不创建额外 WPF 线程。</summary>
        public void LoadWpfUI(System.Windows.UIElement content)
        {
            if (IsDisposed || Disposing) throw new System.ObjectDisposedException(nameof(TaskPaneHostControl));
            host.Child = content;
        }
        /// <summary>幂等断开 WPF 内容；Office 或父控件已释放 ElementHost 时不再写入 Child。</summary>
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing && !contentDetached)
                {
                    // VSTO 删除窗格与会话清理可能先后调用 Dispose；已销毁的桥接器不能再次设置 Child。
                    contentDetached = true;
                    if (host != null && !host.IsDisposed && !host.Disposing) host.Child = null;
                }
            }
            finally { base.Dispose(disposing); }
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // TaskPaneHostControl
            // 
            this.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.Name = "TaskPaneHostControl";
            this.ResumeLayout(false);

        }
    }
}
