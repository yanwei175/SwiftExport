using System;
using System.Windows.Forms;

namespace SwiftExport.UI.Framework.AppUserControls
{
    public partial class CollapsePanelUserControl : UserControl
    {
        public CollapsePanelUserControl()
        {
            InitializeComponent();
        }

        private void CtrlVisiblePane2_Click(object sender, EventArgs e)
        {
            CollapsePanel.Panel2Collapsed = !CollapsePanel.Panel2Collapsed;
            CtrlVisiblePane2.Text = CollapsePanel.Panel2Collapsed ? "▲ 显示" : "▼ 隐藏";
        }
    }
}
