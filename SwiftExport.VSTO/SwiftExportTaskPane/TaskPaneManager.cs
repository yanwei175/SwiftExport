using Microsoft.Office.Tools;
using System.Collections.Generic;
using Excel = Microsoft.Office.Interop.Excel;

namespace SwiftExport.VSTO.SwiftExportTaskPane
{
    public static class TaskPaneManager
    {
        // 1. 图纸库：存放所有模块的配置声明
        private static readonly Dictionary<TaskPaneId, TaskPaneConfig> ConfigRegistry = new Dictionary<TaskPaneId, TaskPaneConfig>();

        // 2. 成品库：存放每个窗口打开的独立侧边栏实例 (Key 为 Hwnd_PaneId，确保多窗口多模块互不干扰)
        private static readonly Dictionary<string, CustomTaskPane> ActivePanes = new Dictionary<string, CustomTaskPane>();

        // 【新增模块专用接口】把图纸录入系统
        public static void RegisterConfig(TaskPaneConfig config)
        {
            ConfigRegistry[config.PaneId] = config;
        }

        // 【Ribbon 专用接口】一键召唤侧边栏
        // ★ 核心改变：把 ShowPane 变成了 TogglePane (开关模式)
        public static void TogglePane(Excel.Window window, TaskPaneId paneId)
        {
            if (window == null || !ConfigRegistry.ContainsKey(paneId)) return;

            string instanceKey = $"{window.Hwnd}_{paneId}";
            CustomTaskPane pane = null;

            // 清理“僵尸面板”
            // 如果字典里有记录，但这个面板在底层已经被 Excel 意外销毁（比如关过窗口），我们要把它清掉
            if (ActivePanes.ContainsKey(instanceKey))
            {
                try
                {
                    // 故意碰一下它的属性，如果底层被销毁了，这里会抛出异常
                    bool testState = ActivePanes[instanceKey].Visible;
                }
                catch
                {
                    // 发现僵尸，清理门户！
                    ActivePanes.Remove(instanceKey);
                }
            }

            // ================= 核心流转逻辑 =================

            // 1. 判断侧边栏是否已经创建过
            if (ActivePanes.ContainsKey(instanceKey))
            {
                // 🎯 命中缓存：复用已有的侧边栏
                pane = ActivePanes[instanceKey];

                if (!pane.Visible)
                {
                    // ✅极其干净：既然是缓存的，它自己记得宽度和位置，直接显示即可！
                    pane.Visible = true;
                }
                else
                {
                    pane.Visible = false;
                }
            }
            else
            {
                // 🎯 未命中缓存：说明真的是第一次打开，执行新建
                var config = ConfigRegistry[paneId];
                var hostControl = new TaskPaneHostControl();
                hostControl.LoadWpfUI(config.WpfControlCreator());

                // 此时 VSTO 默认把它放在右边，但此时 Visible 是 false，用户看不见
                pane = Globals.ThisAddIn.CustomTaskPanes.Add(hostControl, config.Title, window);

                // 趁它还没露脸，在黑暗中把它挪到指定的左边/右边
                pane.DockPosition = config.DockPosition;
                if (config.DockPosition == Microsoft.Office.Core.MsoCTPDockPosition.msoCTPDockPositionLeft ||
                    config.DockPosition == Microsoft.Office.Core.MsoCTPDockPosition.msoCTPDockPositionRight)
                {
                    pane.Width = config.DefaultWidth;
                }

                ActivePanes.Add(instanceKey, pane);

                // 位置摆好后，再拉开幕布！
                pane.Visible = true;
            }
        }


        public static void ChangeDockPosition(Excel.Window window, TaskPaneId paneId, Microsoft.Office.Core.MsoCTPDockPosition newPosition)
        {
            if (window == null) return;
            string instanceKey = $"{window.Hwnd}_{paneId}";

            // 找到当前窗口对应的侧边栏，直接修改位置
            if (ActivePanes.ContainsKey(instanceKey))
            {
                var pane = ActivePanes[instanceKey];
                pane.DockPosition = newPosition;
            }
        }


    }
}
