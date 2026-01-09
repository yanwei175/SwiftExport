using System;
using System.Windows.Forms;

namespace SwiftExport.UI.Framework.Common
{
    public static class ThreadHelper
    {
        /// <summary>
        /// 这是一个“注入点”。
        /// 宿主程序（WinForm 或 VSTO）在启动时，要把自己的“Invoke”逻辑赋值给这个属性。
        /// </summary>
        public static Action<Action> MainThreadDispatcher { get; set; } = (action) =>
        {
            // 默认兜底逻辑：如果没注入，就尝试自己找窗体
            if (Application.OpenForms.Count > 0 && Application.OpenForms[0].InvokeRequired)
            {
                Application.OpenForms[0].Invoke(action);
            }
            else
            {
                action();
            }
        };

        /// <summary>
        /// 通用调用方法：无论在哪，调用这个就能回主线程
        /// </summary>
        public static void UIInvoke(Action action)
        {
            MainThreadDispatcher?.Invoke(action);
        }
    }
}