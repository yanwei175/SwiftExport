using System.Windows.Forms;
using System.Windows.Forms.Integration;

namespace SwiftExport.VSTO.SwiftExportTaskPane
{
    public partial class TaskPaneHostControl : UserControl
    {
        // 对外暴露 WPF 主界面
        private ElementHost _host;
        // 对外暴露内部的 WPF 控件，方便以后传数据
        public System.Windows.Controls.UserControl WpfContent => _host.Child as System.Windows.Controls.UserControl;
        public TaskPaneHostControl()
        {
            InitializeComponent();

            _host = new ElementHost { Dock = DockStyle.Fill };
            this.Controls.Add(_host);
        }
        // 把任意的 WPF 界面塞进桥接器
        public void LoadWpfUI(System.Windows.Controls.UserControl wpfUI)
        {
            _host.Child = wpfUI;
        }
    }
}
