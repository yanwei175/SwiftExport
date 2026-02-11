using System;
using System.Linq;
using System.Windows;

namespace SwiftExport.VSTO.Common
{
    public static class WindowHelper
    {
        /// <summary>
        /// 泛型方法：确保窗口只打开一个实例
        /// </summary>
        /// <typeparam name="T">窗口类型</typeparam>
        /// <param name="factory">如果没找到，如何创建它的函数 (也就是 new 的逻辑)</param>
        public static void ShowUnique<T>(Func<T> factory) where T : Window
        {
            // 1. 去当前所有打开的窗口里找，有没有 T 类型的？
            var existingWindow = Application.Current.Windows.OfType<T>().FirstOrDefault();

            if (existingWindow != null)
            {
                // 2. 如果找到了：
                // 如果最小化了，还原它
                if (existingWindow.WindowState == WindowState.Minimized)
                {
                    existingWindow.WindowState = WindowState.Normal;
                }

                // 把它激活到最前
                existingWindow.Activate();
            }
            else
            {
                // 3. 如果没找到：
                // 调用传入的 factory 函数来创建新窗口 (这里执行 new，速度极快)
                var newWindow = factory();
                newWindow.Show();
            }
        }









    }
}
