using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Office.Interop.Excel;
using SwiftExport.AppLayer.DI;
using SwiftExport.UiKongJianFrameWork;
using SwiftExport.UiKongJianFrameWork.AppForms;
using SwiftExportAddIn.DI;
using SwiftExportAddIn.TaskPanel;
using SwiftExportAddIn.UserFactory;
using SwiftExportVSTO;
using SwiftExportVSTO.静态类;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Excel = Microsoft.Office.Interop.Excel;


namespace SwiftExportAddIn
{
    public partial class ThisAddIn
    {
        private void ThisAddIn_Startup(object sender, System.EventArgs e)
        {
            Application.WorkbookBeforeClose += DeleteTaskPenByWindowsClose;

            var provider = DI_VstoHost.Provider;

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
