using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace SwiftExport.Client.VSTO.Resources.Controls
{
    /// <summary>导入单头的原生 Popup：统一关闭命令、Esc 和卸载清理，不拥有编辑模型。</summary>
    public sealed class ImportHeaderPopup : Popup
    {
        private readonly CommandBinding closeBinding;
        private FrameworkElement target;
        private UIElement content;
        private bool suppressToggleClick;

        /// <summary>使用原生外部点击关闭机制，关闭时仍保留弹层中的输入与绑定。</summary>
        public ImportHeaderPopup()
        {
            StaysOpen = false;
            closeBinding = new CommandBinding(ApplicationCommands.Close, OnCloseExecuted);
            Loaded += OnPopupLoaded;
            Unloaded += OnPopupUnloaded;
        }

        /// <summary>弹层打开后接线关闭操作，并将键盘焦点交给弹层内容。</summary>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            AttachTarget();
            content = Child;
            if (content == null) return;
            content.CommandBindings.Add(closeBinding);
            content.KeyDown += OnContentKeyDown;
            content.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }

        /// <summary>解除本次内容事件；识别按钮外部点击，防止自动关闭后同一次点击又重新打开。</summary>
        protected override void OnClosed(EventArgs e)
        {
            suppressToggleClick = Mouse.LeftButton == MouseButtonState.Pressed && target != null && target.IsMouseOver;
            if (content != null)
            {
                content.KeyDown -= OnContentKeyDown;
                content.CommandBindings.Remove(closeBinding);
                content = null;
            }
            base.OnClosed(e);
        }

        // Popup 的 HWND 与页面可视树独立；入口卸载时必须主动关闭，避免留下浮动窗口。
        private void OnPopupLoaded(object sender, RoutedEventArgs e) { AttachTarget(); }
        private void OnPopupUnloaded(object sender, RoutedEventArgs e)
        {
            SetCurrentValue(IsOpenProperty, false);
            DetachTarget();
        }

        /// <summary>只订阅当前入口的视图事件，不持有宿主句柄或 COM 对象。</summary>
        private void AttachTarget()
        {
            if (ReferenceEquals(target, PlacementTarget)) return;
            DetachTarget();
            target = PlacementTarget as FrameworkElement;
            if (target == null) return;
            target.PreviewMouseLeftButtonDown += OnTargetMouseDown;
            target.PreviewMouseLeftButtonUp += OnTargetMouseUp;
            target.Unloaded += OnPopupUnloaded;
        }

        /// <summary>页面卸载或入口变更时解除订阅，重新显示时可再接线。</summary>
        private void DetachTarget()
        {
            if (target == null) return;
            target.PreviewMouseLeftButtonDown -= OnTargetMouseDown;
            target.PreviewMouseLeftButtonUp -= OnTargetMouseUp;
            target.Unloaded -= OnPopupUnloaded;
            target = null;
            suppressToggleClick = false;
        }

        /// <summary>再次点击入口只关闭，不允许原生外部关闭与 ToggleButton 切换叠加成重新打开。</summary>
        private void OnTargetMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsOpen && !suppressToggleClick) return;
            SetCurrentValue(IsOpenProperty, false);
            suppressToggleClick = false;
            e.Handled = true;
        }

        private void OnTargetMouseUp(object sender, MouseButtonEventArgs e) { suppressToggleClick = false; }

        /// <summary>子下拉或日期弹层优先处理自己的 Esc；未处理的 Esc 关闭当前单头弹层。</summary>
        private void OnContentKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            CloseAndRestoreFocus();
            e.Handled = true;
        }

        private void OnCloseExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            CloseAndRestoreFocus();
            e.Handled = true;
        }

        /// <summary>显式关闭返回入口焦点；外部点击关闭不抢回用户在 Excel 或其他控件上的焦点。</summary>
        private void CloseAndRestoreFocus()
        {
            SetCurrentValue(IsOpenProperty, false);
            target?.Focus();
        }
    }
}
