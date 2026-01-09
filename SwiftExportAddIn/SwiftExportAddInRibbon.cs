using Microsoft.Office.Tools.Ribbon;
using SwiftExport.UI.Framework;
using SwiftExport.UI.Framework.AppForms;
using SwiftExportAddIn.DI;
using SwiftExportAddIn.TaskPanel;
using SwiftExportVSTO.GlobalClass;
using System;
using System.Reflection;
using System.Windows.Forms;


namespace SwiftExportAddIn
{
    public partial class SwiftExportAddInRibbon
    {

        private void SwiftExportAddInRibbon_Load(object sender, RibbonUIEventArgs e)
        {

        }

        /// <summary>
        /// 核心判断逻辑：根据当前 Workbook 决定是否显示 Tab
        /// </summary>
        public void CheckVisibility(Microsoft.Office.Interop.Excel.Workbook wb)
        {
            try
            {
                // =========================================================
                // 智能检测 Visual Studio 调试器
                // =========================================================
                // 只要你按了 F5 (调试模式)，System.Diagnostics.Debugger.IsAttached 就会为 true
                // 这样开发时永远可以看到 Ribbon，不需要改任何配置！
                if (System.Diagnostics.Debugger.IsAttached)
                {
                    this.tab2.Visible = true;
                    return;
                }

                // =========================================================
                // 配置文件里的开发模式开关
                // =========================================================
                // 如果你需要测试 Release 版但不想配白名单，就把 JSON 里的 DevelopmentMode 改为 true
                if (AppConfig.IsDevMode)
                {
                    this.tab2.Visible = true;
                    return;
                }

                // =========================================================
                // 下面是正式环境的白名单逻辑 (保持不变)
                // =========================================================
                var wl = GlobalCom.WhitelistCache;

                // 1. 如果 whitelist 为 null，说明配置里没开启白名单功能，直接显示
                if (wl == null)
                {
                    this.tab2.Visible = true;
                    return;
                }

                // 2. 如果没有打开任何 Workbook，隐藏
                if (wb == null)
                {
                    this.tab2.Visible = false;
                    return;
                }

                // 3. 判断文件名
                string currentName = wb.FullName;

                // 忽略大小写比较
                bool isAllowed = wl.Contains(currentName);

                this.tab2.Visible = isAllowed;
            }
            catch (Exception)
            {
                // 容错：如果出错，默认显示，以免插件彻底不可用
                this.tab2.Visible = true;
                // 记录日志...
            }
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
            // 显示登录窗体作为对话框
            var loginForm = DI_VstoHost.GetService<LoginFrm>();
            if (loginForm.ShowDialog() == DialogResult.OK)
            {
                // 登录成功后启动主窗体
                LoginOk();
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
            //if (this.tab2.Visible == false) return;

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
            Type t = asm.GetType($"SwiftExportAddIn.用户控件.{typeName}") ?? throw new Exception($"未找到类型: {typeName}");

            // 创建实例
            object obj = Activator.CreateInstance(t);

            // 转换为 UserControl
            return obj as UserControl
                   ?? throw new Exception($"类型 {typeName} 不是 UserControl");
        }

        private void Btn_BaseSetting_FrmCustomersManager_Click(object sender, RibbonControlEventArgs e)
        {
            Form frm1 = DI_VstoHost.GetService<FrmCustomersManager>();
            frm1.Show();
        }

        private void Btn_BaseSetting_FrmSupplierManager_Click(object sender, RibbonControlEventArgs e)
        {
            Form frm1 = DI_VstoHost.GetService<FrmSuppliersManager>();
            //Form frm1 = new TestForm();
            frm1.Show();
        }

        private void Btn_BaseSetting_FrmProductColorManager_Click(object sender, RibbonControlEventArgs e)
        {
            Form frm1 = DI_VstoHost.GetService<FrmProductColorManager>();
            //Form frm1 = new TestForm();
            frm1.Show();
        }

        private void Btn_BaseSetting_FrmExcelFieldMappManager_Click(object sender, RibbonControlEventArgs e)
        {
            Form frm1 = DI_VstoHost.GetService<FrmExcelSheetFieldsMappingManager>();
            //Form frm1 = new TestForm();
            frm1.Show();
        }
    }
}

