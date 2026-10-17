using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Xaml.Behaviors;

namespace SwiftExportClient.Resources.Behaviors;

/// <summary>管理筛选弹窗焦点、关闭键及鼠标路由边界，不参与筛选或业务状态。</summary>
public sealed class FilterPopupFocusBehavior : Behavior<Popup>
{
    private DispatcherOperation? _focusOperation;

    /// <summary>挂接弹窗生命周期；行为卸载时解除所有订阅。</summary>
    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.Opened += OnOpened;
        AssociatedObject.Closed += OnClosed;
        AssociatedObject.PreviewKeyDown += OnKeyDown;
        AssociatedObject.MouseLeftButtonDown += OnPopupMouseButton;
        AssociatedObject.MouseLeftButtonUp += OnPopupMouseButton;
        AssociatedObject.IsKeyboardFocusWithinChanged += OnFocusChanged;
        AssociatedObject.Unloaded += OnUnloaded;
    }

    /// <summary>取消排队焦点操作，防止卸载后回调旧弹窗。</summary>
    protected override void OnDetaching()
    {
        CancelFocus();
        AssociatedObject.Opened -= OnOpened;
        AssociatedObject.Closed -= OnClosed;
        AssociatedObject.PreviewKeyDown -= OnKeyDown;
        AssociatedObject.MouseLeftButtonDown -= OnPopupMouseButton;
        AssociatedObject.MouseLeftButtonUp -= OnPopupMouseButton;
        AssociatedObject.IsKeyboardFocusWithinChanged -= OnFocusChanged;
        AssociatedObject.Unloaded -= OnUnloaded;
        base.OnDetaching();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        CancelFocus();
        var popup = AssociatedObject;
        _focusOperation = popup.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            _focusOperation = null;
            if (popup.IsOpen && popup.Child is UIElement child)
                child.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        });
    }

    /// <summary>键盘焦点离开弹窗时同步关闭，避免仅鼠标失焦能关闭的差异。</summary>
    private void OnFocusChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (Equals(e.NewValue, false) && AssociatedObject.IsOpen)
            AssociatedObject.SetCurrentValue(Popup.IsOpenProperty, false);
    }

    private void OnClosed(object? sender, EventArgs e) => CancelFocus();
    private void OnUnloaded(object sender, RoutedEventArgs e) => CancelFocus();

    /// <summary>结束弹窗交互；IsOpen 双向绑定让库恢复原表格焦点。</summary>
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Escape or Key.Enter or Key.Tab)) return;
        AssociatedObject.PlacementTarget?.Focus();
        AssociatedObject.SetCurrentValue(Popup.IsOpenProperty, false);
        e.Handled = true;
    }

    /// <summary>仅截断子控件未处理的冒泡点击，避免弹窗空白区域触发宿主列头排序。</summary>
    private void OnPopupMouseButton(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void CancelFocus()
    {
        _focusOperation?.Abort();
        _focusOperation = null;
    }
}
