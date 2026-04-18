using System;
using System.Windows.Interop;

namespace SwiftExport.VSTO.UserWindow.Dialog
{
    public static class Msg
    {
        /// <summary>
        /// 显示普通信息 (绿色图标)
        /// </summary>
        public static void ShowInfo(string message, string title = "提示")
        {
            Show(message, title, MsgType.Info);
        }

        /// <summary>
        /// 显示警告信息 (黄色图标)
        /// </summary>
        public static void ShowWarning(string message, string title = "警告")
        {
            Show(message, title, MsgType.Warning);
        }

        /// <summary>
        /// 显示错误信息 (红色图标)
        /// </summary>
        public static void ShowError(string message, string title = "错误")
        {
            Show(message, title, MsgType.Error);
        }

        /// <summary>
        /// 显示确认询问框 (蓝色图标, Yes/No)
        /// </summary>
        /// <returns>点击确定返回 true, 点击取消返回 false</returns>
        public static bool ShowQuestion(string message, string title = "确认操作")
        {
            return Show(message, title, MsgType.Question);
        }

        // 私有通用显示方法
        private static bool Show(string message, string title, MsgType type)
        {
            // 1. 在 UI 线程创建窗口
            var win = new MsgBoxWindow(message, title, type);

            // 2. 设置 Excel 为父窗口 (防止窗口跑到 Excel 后面去)
            // 务必引用 Globals.ThisAddIn
            try
            {
                var xlApp = Globals.ThisAddIn.Application;
                var helper = new WindowInteropHelper(win)
                {
                    Owner = new IntPtr(xlApp.Hwnd)
                };
            }
            catch
            {
                // 防御性编程：万一获取不到句柄，只影响层级，不影响功能
            }

            // 3. 模态显示
            win.ShowDialog();

            return win.Result;
        }

        /// <summary>
        /// 显示输入框
        /// </summary>
        /// <param name="message">提示信息</param>
        /// <param name="title">标题</param>
        /// <param name="defaultText">默认值</param>
        /// <returns>用户输入的字符串，如果取消则返回 null</returns>
        public static string ShowInput(string message, string title = "请输入", string defaultText = "")
        {
            var win = new InputBoxWindow(message, title, defaultText);

            try
            {
                var xlApp = Globals.ThisAddIn.Application;
                var helper = new WindowInteropHelper(win)
                {
                    Owner = new IntPtr(xlApp.Hwnd)
                };
            }
            catch { }

            win.ShowDialog();

            // 如果用户点了确定，返回输入的文本；否则返回 null
            return win.IsConfirmed ? win.InputText : null;
        }





    }
}