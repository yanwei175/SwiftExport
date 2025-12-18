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
using SwiftExport.UiKongJianFrameWork.AppModels;
namespace SwiftExport.UiWinForm
{
    public partial class MainFrm : Form
    {
        private readonly IServiceProvider _Provider = Program.ServiceProvider;
        public MainFrm()
        {
            InitializeComponent();
        }

        private void Button1_Click(object sender, EventArgs e)
        {
            Form frm1 = new SalesOrderManagerForm();
            frm1.Show();
        }

        private void CustomerSetting_Click(object sender, EventArgs e)
        {
            Form frm1 = _Provider.GetRequiredService<FrmCustomersManager>();
            //Form frm1 = new TestForm();
            frm1.Show();
        }

        private void 供应商管理ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Form frm1 = _Provider.GetRequiredService<FrmSuppliersManager>();
            //Form frm1 = new TestForm();
            frm1.Show();
        }
    }
}
