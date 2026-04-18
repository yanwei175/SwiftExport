using System;
using System.Threading.Tasks;

namespace MateWpfThemes.FW.Services.Dialogs
{
    public static class DialogProvider
    {
        private static Controls.DialogHost _currentHost;

        internal static void RegisterHost(Controls.DialogHost host)
        {
            _currentHost = host;

            // 中文注释：注册 Host 时顺手初始化控件库级 Dispatcher。
            // 这样在宿主忘记显式 Initialize 的情况下，也能有可用的 UI 调度器。
            if (host != null)
            {
                DispatcherProvider.Initialize(host.Dispatcher);
            }
        }

        /// <summary>
        /// 统一 UI 线程执行入口：
        /// 1) 全部通过控件库 DispatcherProvider 执行；
        /// 2) 若尚未初始化且存在 Host，则先用 Host.Dispatcher 注入；
        /// 3) 最终仍由 DispatcherProvider 负责具体调度逻辑。
        /// </summary>
        private static void RunOnUi(Action action)
        {
            if (action == null) return;

            // 中文注释：兜底注入一次，避免宿主忘记显式 Initialize。
            if (DispatcherProvider.UI == null && _currentHost != null)
            {
                DispatcherProvider.Initialize(_currentHost.Dispatcher);
            }

            DispatcherProvider.Invoke(action);
        }

        private static void CloseDialog()
        {
            if (_currentHost != null)
            {
                RunOnUi(() =>
                {
                    _currentHost.IsOpen = false;
                });
            }
        }

        /// <summary>
        /// 弹出消息类 (Info / Warning / Error)，只有一个确定按钮
        /// </summary>
        public static Task ShowMessageAsync(string message, string title, DialogIcon icon)
        {
            var tcs = new TaskCompletionSource<bool>();
            if (_currentHost == null) { tcs.TrySetException(new Exception("UI 树中未找到 DialogHost")); return tcs.Task; }

            RunOnUi(() =>
            {
                var vm = new MsgBoxViewModel
                {
                    Title = title,
                    Message = message,
                    Icon = icon,
                    ShowCancelButton = false, // 消息框不需要取消按钮
                    ConfirmText = "我知道了",
                    OnConfirm = () => { CloseDialog(); tcs.TrySetResult(true); },
                    OnCancel = () => { CloseDialog(); tcs.TrySetResult(false); } // 兼容按下 Esc
                };
                _currentHost.DialogContent = vm;
                _currentHost.IsOpen = true;
            });
            return tcs.Task;
        }

        /// <summary>
        /// 弹出确认框 (VSTO 线程安全版)
        /// </summary>
        public static Task<bool> ShowConfirmAsync(string message, string title)
        {
            var tcs = new TaskCompletionSource<bool>();

            // 先拦截为空的情况
            if (_currentHost == null)
            {
                tcs.TrySetException(new Exception("UI 树中未找到 DialogHost 控件！请确保 XAML 中已放置 <fw:DialogHost />"));
                return tcs.Task;
            }

            RunOnUi(() =>
            {
                var vm = new MsgBoxViewModel
                {
                    Title = title,
                    Message = message,
                    Icon = DialogIcon.Question,
                    ShowCancelButton = true,
                    OnConfirm = () => { CloseDialog(); tcs.TrySetResult(true); },
                    OnCancel = () => { CloseDialog(); tcs.TrySetResult(false); }
                };

                _currentHost.DialogContent = vm;
                _currentHost.IsOpen = true;
            });

            return tcs.Task;
        }

        /// <summary>
        /// 弹出输入框 (VSTO 线程安全版)
        /// </summary>
        public static Task<string> ShowInputAsync(string message, string title)
        {
            var tcs = new TaskCompletionSource<string>();

            if (_currentHost == null)
            {
                tcs.TrySetException(new Exception("UI 树中未找到 DialogHost 控件！"));
                return tcs.Task;
            }

            RunOnUi(() =>
            {
                var vm = new InputBoxViewModel
                {
                    Message = message,
                    Title = title,
                    OnSubmit = (text) => { CloseDialog(); tcs.TrySetResult(text); },
                    OnCancel = () => { CloseDialog(); tcs.TrySetResult(null); }
                };

                _currentHost.DialogContent = vm;
                _currentHost.IsOpen = true;
            });

            return tcs.Task;
        }
    }
}