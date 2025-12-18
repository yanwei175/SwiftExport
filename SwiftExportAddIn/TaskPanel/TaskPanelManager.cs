using Microsoft.Office.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace SwiftExportAddIn.TaskPanel
{

    public partial class TaskPanelManager
    {
        // 用窗口句柄作为 Key，缓存所有 TaskPane
        public static Dictionary<int, CustomTaskPane> TaskPanelDict = new Dictionary<int, CustomTaskPane>();

        // 当前激活窗口句柄
        public static int CurrentWindowHwnd = -1;

        public static bool ExistsTaskPane(int hwnd)
        {
            return TaskPanelDict.ContainsKey(hwnd);
        }

        public static CustomTaskPane GetTaskPane(int hwnd)
        {
            return TaskPanelDict.ContainsKey(hwnd) ? TaskPanelDict[hwnd] : null;
        }

        public static CustomTaskPane CreateEmptyTaskPane(Microsoft.Office.Interop.Excel.Window wn)
        {
            var hwnd = wn.Hwnd;
            if (!TaskPanelDict.TryGetValue(hwnd, out var pane))
            {
                var uc = new UserControl();
                pane = Globals.ThisAddIn.CustomTaskPanes.Add(uc, "SwiftExport", wn);
                pane.Visible = false;
                AddTaskPane(hwnd, pane);
            }
            return pane;
        }

        public static CustomTaskPane CreateTaskPane(UserControl uc, Microsoft.Office.Interop.Excel.Window wn)
        {
            var hwnd = wn.Hwnd;
            if (!TaskPanelDict.TryGetValue(hwnd, out var pane))
            {
                pane = Globals.ThisAddIn.CustomTaskPanes.Add(uc, "SwiftExport", wn);
                pane.Visible = false;
                AddTaskPane(hwnd, pane);
            }
            return pane;
        }

        public static void RemoveTaskPane(int hwnd)
        {
            if (TaskPanelDict.ContainsKey(hwnd))
            {
                TaskPanelDict[hwnd].Dispose();
                TaskPanelDict.Remove(hwnd);
            }
        }

        private static void AddTaskPane(int hwnd, CustomTaskPane pane)
        {
            if (!TaskPanelDict.ContainsKey(hwnd))
            {
                TaskPanelDict[hwnd] = pane;
            }
        }



    }

}
