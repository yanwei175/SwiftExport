using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace MateWpfThemes.FW.Services.Dialogs
{
    public static class DialogProvider
    {
        // ─────────────────────────────────────────────
        // Toast 通知默认显示时长（毫秒）
        // ─────────────────────────────────────────────
        private const int DefaultToastDurationMs = 3000;

        // 中文注释：用栈管理多层 DialogHost，支持子窗口（如详情弹窗）拥有自己的 Host。
        // 子窗口的 DialogHost 卸载时调用 UnregisterHost 出栈，自动还原到上一层主页 Host。
        // 这是修复「子窗口关闭后主页弹窗不显示」Bug 的核心改动：
        //   根因：_currentHost 被子窗口 Host 覆盖后，子窗口关闭但 _currentHost 仍指向已销毁的 Host。
        private static readonly System.Collections.Generic.Stack<Controls.DialogHost> _hostStack
            = new System.Collections.Generic.Stack<Controls.DialogHost>();

        // 中文注释：当前活动 Host 始终是栈顶元素；栈空时返回 null。
        // 所有依赖 _currentHost 的调用方（ShowMessageAsync 等）无需任何改动。
        private static Controls.DialogHost _currentHost
            => _hostStack.Count > 0 ? _hostStack.Peek() : null;

        // 用于控制多个 Host 的 ZIndex 值，值越大显示越上层
        private static int _zIndexCounter = 1000;

        internal static void RegisterHost(Controls.DialogHost host)
        {
            if (host == null) return;

            // 中文注释：入栈，新 Host 成为当前活动 Host，旧 Host 保留在栈底等待还原。
            _hostStack.Push(host);

            // 中文注释：注册 Host 时顺手初始化控件库级 Dispatcher。
            // 这样在宿主忘记显式 Initialize 的情况下，也能有可用的 UI 调度器。
            DispatcherProvider.Initialize(host.Dispatcher);

            // 注册 Host 时把它放到最上层，避免后续 Host 被遮挡
            BringHostToFront();
        }

        /// <summary>
        /// 中文注释：注销 Host（在 DialogHost.Unloaded 时调用）。
        /// 将该 Host 从栈中移除，自动还原到上一层活动 Host，
        /// 解决子窗口关闭后主页弹窗不显示的问题。
        /// </summary>
        internal static void UnregisterHost(Controls.DialogHost host)
        {
            if (host == null || _hostStack.Count == 0) return;

            if (_hostStack.Peek() == host)
            {
                // 中文注释：最常见情况——被注销的就是栈顶，直接 Pop，上一层 Host 自动成为活动 Host。
                _hostStack.Pop();
            }
            else
            {
                // 中文注释：极少数情况：非栈顶 Host 先销毁，重建栈以保留其余 Host 的原始顺序。
                var remaining = new System.Collections.Generic.List<Controls.DialogHost>();
                foreach (var h in _hostStack)
                    if (h != host) remaining.Add(h);

                _hostStack.Clear();
                remaining.Reverse(); // 还原原始入栈顺序（底层在前）
                foreach (var h in remaining)
                    _hostStack.Push(h);
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

        // 将当前 Host 提到最上层
        private static void BringHostToFront()
        {
            if (_currentHost == null) return;
            // 使用 Interlocked 保证线程安全的自增
            int newIndex = Interlocked.Increment(ref _zIndexCounter);
            // 设置附加属性 Panel.ZIndex，使得后显示的 Host 能覆盖在前面的之上
            Panel.SetZIndex(_currentHost, newIndex);
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
                // 确保当前 Host 在最上层再打开模态窗
                BringHostToFront();
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

                // 确保当前 Host 在最上层再打开模态窗
                BringHostToFront();
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

                // 确保当前 Host 在最上层再打开模态窗
                BringHostToFront();
                _currentHost.DialogContent = vm;
                _currentHost.IsOpen = true;
            });

            return tcs.Task;
        }

        // ─────────────────────────────────────────────
        // Toast 非阻塞通知
        // ─────────────────────────────────────────────

        /// <summary>
        /// 发出一条轻量级 Toast 通知：
        /// 不阻塞 UI，自动在 <paramref name="durationMs"/> 毫秒后消失。
        /// 多条通知会同时排队显示在屏幕上。
        /// </summary>
        /// <param name="message">通知正文</param>
        /// <param name="icon">图标类型（Info / Warning / Error）</param>
        /// <param name="durationMs">显示时长，毫秒，默认 3000</param>
        public static void Notify(string message, int durationMs = DefaultToastDurationMs, DialogIcon icon = DialogIcon.Info)
        {
            // 没有 Host 时静默忽略，不抛异常（Toast 是非关键通知）
            if (_currentHost == null) return;

            // 在 UI 线程上创建 ViewModel 并加入队列
            MsgBoxViewModel toastVm = null;
            RunOnUi(() =>
            {
                toastVm = new MsgBoxViewModel
                {
                    Message = message,
                    Icon = icon,
                    ShowCancelButton = false
                };
                _currentHost.ActiveToasts.Add(toastVm);
            });

            // 在后台线程等待，到期后回到 UI 线程移除——不阻塞调用方
            Task.Delay(durationMs).ContinueWith(_ =>
            {
                RunOnUi(() =>
                {
                    if (toastVm != null)
                        _currentHost?.ActiveToasts.Remove(toastVm);
                });
            });
        }
    }
}