using SwiftExport.UI.Framework.AppForms;
using SwiftExport.UI.Framework.DialogsForms;
using SwiftExport.UI.Framework.ManagerForms;
using SwiftExport.UiWinForm.DI;
namespace SwiftExport.UiWinForm
{
    public partial class MainFrm : Form
    {
        public MainFrm()
        {
            InitializeComponent();
        }

        private void Button1_Click(object sender, EventArgs e)
        {
        }

        private void CustomerSetting_Click(object sender, EventArgs e)
        {
            Form frm1 = DI_WinFormHost.GetService<FrmCustomersManager>();
            //Form frm1 = new TestForm();
            frm1.ShowDialog();
        }

        private void 供应商管理ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Form frm1 = DI_WinFormHost.GetService<FrmSuppliersManager>();
            //Form frm1 = new TestForm();
            frm1.ShowDialog();
        }

        private void Excel字段映射管理ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Form frm1 = DI_WinFormHost.GetService<FrmExcelSheetFieldsMappingManager>();
            //Form frm1 = new TestForm();
            frm1.ShowDialog();
        }

        private void 产品颜色管理ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Form frm1 = DI_WinFormHost.GetService<FrmProductColorManager>();
            //Form frm1 = new TestForm();
            frm1.ShowDialog();
        }

        private void 产品管理ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Form frm1 = DI_WinFormHost.GetService<FrmProductManager>();
            //Form frm1 = new TestForm();
            frm1.ShowDialog();
        }

        private void WhiteListManageTSMenuItem_Click(object sender, EventArgs e)
        {
            Form frm1 = DI_WinFormHost.GetService<FrmWhiteListManager>();
            //Form frm1 = new TestForm();
            frm1.ShowDialog();
        }

        private void 测试按钮ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Form frm1 = DI_WinFormHost.GetService<FrmProductSelector>();
            //Form frm1 = new TestForm();
            frm1.ShowDialog();
        }
    }
}
