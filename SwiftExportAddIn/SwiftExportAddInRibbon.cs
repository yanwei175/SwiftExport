using Microsoft.Extensions.DependencyInjection;
using Microsoft.Office.Tools.Ribbon;
using SwiftExport.UiKongJianFrameWork;
using SwiftExport.UiKongJianFrameWork.AppModels;
using SwiftExportAddIn.TaskPanel;
using SwiftExportAddIn.UserFactory;
using SwiftExportVSTO;
using SwiftExportVSTO.静态类;
using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace SwiftExportAddIn
{
    public partial class SwiftExportAddInRibbon
    {

        private void SwiftExportAddInRibbon_Load(object sender, RibbonUIEventArgs e)
        {
            
        }

        private void Rbn_btn_Product_Click(object sender, RibbonControlEventArgs e)
        {
            int hwnd = Globals.ThisAddIn.Application.ActiveWindow.Hwnd;

            if (!TaskPanelManager.ExistsTaskPane(hwnd))
            {
                var tskp = TaskPanelManager.CreateEmptyTaskPane(Globals.ThisAddIn.Application.ActiveWindow);
                tskp.Visible = true;
                return;
            }
            else
            {
                var tskp = TaskPanelManager.GetTaskPane(hwnd);
                tskp.Visible = !tskp.Visible;
            }
        }

        private void Banna_Login_Click(object sender, RibbonControlEventArgs e)
        {
            var ServiceProvider = Globals.ThisAddIn.ServerFactory.CreateScope().ServiceProvider;

            using (ServiceProvider as IDisposable)
            {
                // 显示登录窗体作为对话框
                var loginForm = ServiceProvider.GetRequiredService<LoginFrm>();
                if (loginForm.ShowDialog() == DialogResult.OK)
                {
                    // 登录成功后启动主窗体
                    LoginOk();
                }
            }
        }

        private void LoginOk()
        {
            this.Rbon_Product_Group.Visible = true;
            this.Banna_Login.Visible = false;
        }

        private void Rbn_btn_SaleProduct_Click(object sender, RibbonControlEventArgs e)
        {
                var pdUserControl = Globals.ThisAddIn.ServerFactory.GetSingletonService < ProductUserControl>();
                Form frm1 = new test();
                frm1.Controls.Add(pdUserControl);
                pdUserControl.Dock = DockStyle.Fill;
                frm1.Show();
        }

        private void Rbn_btn_BOM_Click(object sender, RibbonControlEventArgs e)
        {

        }

        private void Rbn_btn_PurchaseProduct_Click(object sender, RibbonControlEventArgs e)
        {
            int hwnd = Globals.ThisAddIn.Application.ActiveWindow.Hwnd;

            if (!TaskPanelManager.ExistsTaskPane(hwnd))
            {
                var tskp = TaskPanelManager.CreateEmptyTaskPane(Globals.ThisAddIn.Application.ActiveWindow);
                tskp.Visible = true;
                return;
            }
            else
            {
                var tskp = TaskPanelManager.GetTaskPane(hwnd);
                tskp.Visible = !tskp.Visible;
            }
        }

        private void Rbn_btn_GenJinOrder_Click(object sender, RibbonControlEventArgs e)
        {
           Form frm1 =new test();
            frm1.Show();
        }
    }
 }

