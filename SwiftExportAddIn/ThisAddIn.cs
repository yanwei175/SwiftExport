using Microsoft.Office.Interop.Excel;
using SwiftExport.UI.Framework.Common;
using SwiftExportAddIn.DI;
using SwiftExportAddIn.TaskPanel;
using SwiftExportVSTO.GlobalClass;
using System.Windows.Forms;
using Excel = Microsoft.Office.Interop.Excel;


namespace SwiftExportAddIn
{
    public partial class ThisAddIn
    {
        private void ThisAddIn_Startup(object sender, System.EventArgs e)
        {
            Application.WorkbookBeforeClose += DeleteTaskPenByWindowsClose;

            // 监听窗口激活事件（切换 Workbook 时触发）
            this.Application.WindowActivate += Application_WindowActivate;

            var provider = DI_VstoHost.Provider;

            // 初始化全局变量
            _ = GlobalCom.InitializeAsync();

            // 核心：注入 VSTO 的线程同步逻辑
            ThreadHelper.MainThreadDispatcher = (action) =>
            {
                // 优先找活跃窗体（比如你刚打开的 FrmCustomersManager）
                var activeForm = System.Windows.Forms.Form.ActiveForm;

                // 或者找任意打开的窗体
                if (activeForm == null && System.Windows.Forms.Application.OpenForms.Count > 0)
                {
                    activeForm = System.Windows.Forms.Application.OpenForms[0];
                }

                if (activeForm != null && activeForm.InvokeRequired)
                {
                    activeForm.Invoke(action);
                }
                else
                {
                    // 如果实在找不到窗体，或者已经在主线程了，直接执行
                    action();
                }
            };

        }

        private void Application_WindowActivate(Workbook Wb, Window Wn)
        {
            // 调用 Ribbon 中的刷新逻辑
            Globals.Ribbons.SwiftExportAddInRibbon.CheckVisibility(Wb);
        }

        private void ThisAddIn_Shutdown(object sender, System.EventArgs e)
        {

        }


        private void DeleteTaskPenByWindowsClose(Excel.Workbook Wb, ref bool Cancel)
        {
            foreach (Excel.Window awn in Wb.Windows)
            {
                int hwnd = awn.Hwnd;
                TaskPanelManager.RemoveTaskPane(hwnd);
            }
        }



        #region VSTO 生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InternalStartup()
        {
            this.Startup += new System.EventHandler(ThisAddIn_Startup);
            this.Shutdown += new System.EventHandler(ThisAddIn_Shutdown);
        }

        #endregion
    }
}
