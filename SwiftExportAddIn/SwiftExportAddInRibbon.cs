using Microsoft.Extensions.DependencyInjection;
using Microsoft.Office.Tools;
using Microsoft.Office.Tools.Ribbon;
using SwiftExport.UiKongJianFrameWork;
using SwiftExport.UiKongJianFrameWork.AppForms;
using SwiftExportAddIn.TaskPanel;
using SwiftExportAddIn.UserFactory;
using SwiftExportVSTO;
using SwiftExportVSTO.静态类;
using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.Linq;
using System.Reflection;
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
                //var pdUserControl = Globals.ThisAddIn.ServerFactory.GetSingletonService < ProductUserControl>();
                //Form frm1 = new test();
                //frm1.Controls.Add(pdUserControl);
                //pdUserControl.Dock = DockStyle.Fill;
                //frm1.Show();
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
           //Form frm1 =new SalesOrderManagerForm();
           // frm1.Show();
        }

        private void Rbn_btn_SaleOrder_Click(object sender, RibbonControlEventArgs e)
        {
            try
            {
                BindingTskPane(sender as RibbonControl);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void BindingTskPane(RibbonControl b)
        {
            string UserCtrlTypeName = b.Parent.Tag.ToString() + "Buttons";

            if (string.IsNullOrEmpty(UserCtrlTypeName)) return;

            UserControl usCtrl = RefUsersControl(UserCtrlTypeName);

            int hwnd = Globals.ThisAddIn.Application.ActiveWindow.Hwnd;

            if (!TaskPanelManager.ExistsTaskPane(hwnd))
            {
                var tskp = TaskPanelManager.CreateTaskPane(usCtrl, Globals.ThisAddIn.Application.ActiveWindow);
                tskp.Visible = true;
                return;
            }
            else
            {
                var tskp = TaskPanelManager.GetTaskPane(hwnd);
                tskp.Visible = !tskp.Visible;
            }
        }
        public static UserControl RefUsersControl(string typeName)
        {
            // 获取当前程序集
            Assembly asm = Assembly.GetExecutingAssembly();

            // 根据字符串获取类型
            Type t = asm.GetType($"SwiftExportAddIn.用户控件.{ typeName}") ?? throw new Exception($"未找到类型: {typeName}");

            // 创建实例
            object obj = Activator.CreateInstance(t);

            // 转换为 UserControl
            return obj as UserControl
                   ?? throw new Exception($"类型 {typeName} 不是 UserControl");
        }

        private void Btn_BaseSetting_FrmCustomersManager_Click(object sender, RibbonControlEventArgs e)
        {
            //Form frm1 = Globals.ThisAddIn.ServerFactory.GetRequiredService<FrmCustomersManager>();
            ////Form frm1 = new TestForm();
            //frm1.Show();
        }

        private void Btn_BaseSetting_FrmSupplierManager_Click(object sender, RibbonControlEventArgs e)
        {
            Form frm1 = Globals.ThisAddIn.ServerFactory.GetSingletonService<FrmSuppliersManager>();
            //Form frm1 = new TestForm();
            frm1.Show();
        }

        private void Btn_BaseSetting_FrmProductColorManager_Click(object sender, RibbonControlEventArgs e)
        {
            Form frm1 = Globals.ThisAddIn.ServerFactory.GetSingletonService<FrmProductColorManager>();
            //Form frm1 = new TestForm();
            frm1.Show();
        }

        private void Btn_BaseSetting_FrmExcelFieldMappManager_Click(object sender, RibbonControlEventArgs e)
        {
            Form frm1 = Globals.ThisAddIn.ServerFactory.GetSingletonService<FrmExcelSheetFieldsMappingManager>();
            //Form frm1 = new TestForm();
            frm1.Show();
        }
    }
 }

