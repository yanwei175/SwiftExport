using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using SwiftExport.UiKongJianFrameWork.AppForms;
using SwiftExport.AppLayer.Interfaces;
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
            frm1.Show();
        }

        private void 供应商管理ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Form frm1 = DI_WinFormHost.GetService<FrmSuppliersManager>();
            //Form frm1 = new TestForm();
            frm1.Show();
        }

        private void Excel字段映射管理ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Form frm1 = DI_WinFormHost.GetService<FrmExcelSheetFieldsMappingManager>();
            //Form frm1 = new TestForm();
            frm1.Show();
        }

        private void 产品颜色管理ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Form frm1 = DI_WinFormHost.GetService<FrmProductColorManager>();
            //Form frm1 = new TestForm();
            frm1.Show();
        }
    }
}
