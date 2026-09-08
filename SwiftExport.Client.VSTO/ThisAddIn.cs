using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Excel = Microsoft.Office.Interop.Excel;
using Office = Microsoft.Office.Core;
using Microsoft.Office.Tools.Excel;

namespace SwiftExport.Client.VSTO
{
    /// <summary>VSTO 生成宿主的手写入口，负责组合启动与停止。</summary>
    public partial class ThisAddIn
    {
        private SwiftExportRibbon ribbon;
        private Bootstrapper.AddInBootstrapper bootstrapper;

        /// <summary>向 Office 提供 XML Ribbon，与 Startup 的先后顺序无关。</summary>
        protected override Office.IRibbonExtensibility CreateRibbonExtensibilityObject()
        {
            if (ribbon == null) ribbon = new SwiftExportRibbon();
            if (bootstrapper != null) ribbon.Attach(bootstrapper);
            return ribbon;
        }

        // 仅负责宿主组合，业务由独立服务承担。
        private void ThisAddIn_Startup(object sender, System.EventArgs e)
        {
            bootstrapper = new Bootstrapper.AddInBootstrapper();
            if (ribbon != null) ribbon.Attach(bootstrapper);
            bootstrapper.Start(this);
        }

        private void ThisAddIn_Shutdown(object sender, System.EventArgs e)
        {
            ribbon?.Dispose();
            bootstrapper?.Dispose();
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
