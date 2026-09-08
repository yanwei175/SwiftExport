using System;
using Office = Microsoft.Office.Core;
using ExcelInterop = Microsoft.Office.Interop.Excel;

namespace SwiftExport.Client.VSTO.Excel.Context
{
    /// <summary>只在 Excel STA 解析 Ribbon 的所属窗口，不长期保存 COM 窗口。</summary>
    public sealed class ExcelContextService
    {
        private readonly ExcelInterop.Application application;
        /// <summary>借用 VSTO 的 Application，不主动释放共享 RCW。</summary>
        public ExcelContextService(ExcelInterop.Application application) { this.application = application; }
        /// <summary>优先使用 Ribbon 上下文，无上下文时使用当前 Excel 窗口。</summary>
        public IntPtr GetOwner(Office.IRibbonControl control)
        {
            var window = control?.Context as ExcelInterop.Window ?? application.ActiveWindow;
            return new IntPtr(window == null ? application.Hwnd : window.Hwnd);
        }
    }
}
