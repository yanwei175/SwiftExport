using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace SwiftExport.VSTO.SwiftExportWindowHelps
{
    public static class WpfWindowHelp
    {
        /// <summary>全屏（1920×920）弹出子窗口</summary>
        public static void OpenCtrlByFullWindow(string title, object content)
            => OpenCtrlByWindow(title, content, 1920, 920);

        /// <summary>指定大小弹出子窗口</summary>
        public static void OpenCtrlByWindow(string title, object content, double width, double height)
        {
            var win = new Window
            {
                Title = title,
                Width = width,
                Height = height,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = content,
                ShowInTaskbar = false
            };
            // 1. 设置父窗口
            var helper = new System.Windows.Interop.WindowInteropHelper(win);
            helper.Owner = new IntPtr(Globals.ThisAddIn.Application.Hwnd);

            // 2. 修复按键响应问题（非常重要！）
            System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(win);

            // 3. 弹出
            win.ShowDialog();
        }
        public static void OpenWindow(Window win)
        {
            // 1. 设置父窗口
            var helper = new System.Windows.Interop.WindowInteropHelper(win);
            helper.Owner = new IntPtr(Globals.ThisAddIn.Application.Hwnd);

            // 2. 修复按键响应问题（非常重要！）
            System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(win);

            // 3. 弹出
            win.ShowDialog();
        }
    }
}
