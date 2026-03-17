using Microsoft.Office.Core;

namespace SwiftExport.VSTO.SwiftExportTaskPane
{
    public class TaskPaneConfig
    {
        // ★ 从 string 变成了强类型的枚举！
        public TaskPaneId PaneId { get; set; }

        public string Title { get; set; }
        public MsoCTPDockPosition DockPosition { get; set; }
        public int DefaultWidth { get; set; }
        public System.Func<System.Windows.Controls.UserControl> WpfControlCreator { get; set; }
    }
}
