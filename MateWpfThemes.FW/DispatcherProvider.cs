using System;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace MateWpfThemes.FW
{
    /// <summary>
    /// MateWpfThemes 级别的 DispatcherProvider。
    /// 用于让控件库在 WPF / VSTO 等不同宿主中统一进行 UI 线程调度。
    /// 由引用方在启动阶段注入主 Dispatcher。
    /// </summary>
    public static class DispatcherProvider
    {
        /// <summary>
        /// UI 主线程 Dispatcher（由宿主应用注入）。
        /// </summary>
        internal static Dispatcher UI { get; private set; }

        /// <summary>
        /// 初始化 UI Dispatcher（宿主应用应在启动时调用一次）。
        /// </summary>
        public static void Initialize(Dispatcher dispatcher)
        {
            UI = dispatcher;
        }

        /// <summary>
        /// 当前线程是否为 UI 线程。
        /// </summary>
        public static bool IsOnUiThread =>
            UI != null && UI.CheckAccess();

        /// <summary>
        /// 同步执行 UI 操作（阻塞调用）。
        /// 若未初始化 Dispatcher，则直接执行。
        /// </summary>
        public static void Invoke(Action action)
        {
            if (action == null) return;

            if (UI == null)
            {
                action();
                return;
            }

            if (UI.CheckAccess())
            {
                action();
            }
            else
            {
                UI.Invoke(action);
            }
        }

        /// <summary>
        /// 异步执行 UI 操作（不阻塞调用线程）。
        /// 若未初始化 Dispatcher，则直接执行。
        /// </summary>
        public static Task InvokeAsync(Action action)
        {
            if (action == null) return Task.CompletedTask;

            if (UI == null)
            {
                action();
                return Task.CompletedTask;
            }

            if (UI.CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }

            return UI.InvokeAsync(action).Task;
        }

        /// <summary>
        /// 安全执行 UI 操作，不向外抛异常。
        /// </summary>
        public static void TryInvoke(Action action)
        {
            try
            {
                Invoke(action);
            }
            catch
            {
            }
        }
    }
}
