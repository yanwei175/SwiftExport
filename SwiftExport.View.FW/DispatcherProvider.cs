using System;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SwiftExport.View.FW
{
    /// <summary>
    /// 企业级 DispatcherProvider
    /// 适用于 WPF / Excel VSTO / Excel-DNA / WinForms + WPF 混合场景
    /// 控件库不关心主线程是谁，由宿主应用注入 Dispatcher
    ///  WPF应用注入主线程
    ///  protected override void OnStartup(StartupEventArgs e){base.OnStartup(e);DispatcherProvider.Initialize(Dispatcher.CurrentDispatcher);}
    ///VSTO应用注入主线程在ThisAddIn_Startup方法中：
    ///var win = new HiddenWpfWindow(); DispatcherProvider.Initialize(win.Dispatcher);
    /// </summary>
    public static class DispatcherProvider
    {
        /// <summary>
        /// UI 主线程 Dispatcher（由宿主应用注入）
        /// </summary>
        internal static Dispatcher UI { get; private set; }

        /// <summary>
        /// 初始化 UI Dispatcher（宿主应用必须调用）
        /// </summary>
        public static void Initialize(Dispatcher dispatcher)
        {
            UI = dispatcher;
        }

        /// <summary>
        /// 当前线程是否为 UI 主线程
        /// </summary>
        public static bool IsOnUiThread =>
            UI != null && UI.CheckAccess();


        /// <summary>
        /// 纯粹的异步分发（发后即忘），性能最高，最适合高频合计逻辑
        /// </summary>
        public static void BeginInvoke(Action action)
        {
            if (UI == null)
            {
                action();
                return;
            }
            // 直接调用原生 BeginInvoke，不需要创建 Task 对象，节省内存
            UI.BeginInvoke(action);
        }



        /// <summary>
        /// 同步执行 UI 操作（阻塞调用）
        /// </summary>
        public static void Invoke(Action action)
        {
            if (UI == null)
            {
                // 没有 UI Dispatcher，直接执行（Excel 场景可能发生）
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
        /// 异步执行 UI 操作（不会阻塞调用线程）
        /// </summary>
        public static Task InvokeAsync(Action action)
        {
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
        /// await DispatcherProvider.SwitchToUi();
        /// 切换到 UI 线程（类似 ConfigureAwait）
        /// </summary>
        public static Task SwitchToUi()
        {
            if (UI == null || UI.CheckAccess())
                return Task.CompletedTask;

            var tcs = new TaskCompletionSource<bool>();
            UI.BeginInvoke(new Action(() => tcs.SetResult(true)));
            return tcs.Task;
        }

        /// <summary>
        /// 安全调用 UI，不抛异常
        /// </summary>
        public static void TryInvoke(Action action)
        {
            try
            {
                Invoke(action);
            }
            catch
            {
                // 记录日志或忽略
            }
        }
    }
}
