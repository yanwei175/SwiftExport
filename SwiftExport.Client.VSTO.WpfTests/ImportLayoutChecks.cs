using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SwiftExport.Client.VSTO.WpfTests
{
    /// <summary>验证三个导入页共用的单头弹层及真实列绑定，不加载 Office。</summary>
    internal static class ImportLayoutChecks
    {
        /// <summary>检查默认列、往返切换、弹层编辑上下文及关闭操作，确保弹层不占明细布局高度。</summary>
        internal static void Run(UserControl view, DataGrid grid, Action<bool, string> check, int expectedPopupCount = 1)
        {
            var toggle = (Wpf.Ui.Controls.ToggleSwitch)view.FindName("ExtraFieldsSwitch");
            var header = (StackPanel)view.FindName("HeaderStackPanel");
            var optional = grid.Columns.Where(column => BindingOperations.IsDataBound(column, DataGridColumn.VisibilityProperty)).ToArray();
            var required = grid.Columns.Except(optional).ToArray();
            var items = grid.ItemsSource;
            var rows = grid.Items.Cast<object>().ToArray();
            var popups = header.Children.OfType<Popup>().ToArray();
            Pump(); view.UpdateLayout();
            check(toggle.IsChecked == false && optional.Length > 0 && optional.All(column => column.Visibility == Visibility.Collapsed),
                "实际开关默认隐藏次要字段，包括非可视树中的 DataGridColumn");
            check(required.All(column => column.Visibility == Visibility.Visible)
                && ScrollViewer.GetHorizontalScrollBarVisibility(grid) == ScrollBarVisibility.Auto,
                "核心字段常显并保留表格横向滚动");
            toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, true); Pump();
            check(optional.All(column => column.Visibility == Visibility.Visible), "打开开关后全部次要列即时显示");
            toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, false); Pump();
            check(optional.All(column => column.Visibility == Visibility.Collapsed) && ReferenceEquals(items, grid.ItemsSource)
                && rows.SequenceEqual(grid.Items.Cast<object>()), "关闭开关不替换或删除已加载明细");
            check(popups.Length == expectedPopupCount && popups.All(popup => !popup.IsOpen)
                && Descendants<ToggleButton>(header).Count(button => button.Name.EndsWith("ToggleButton", StringComparison.Ordinal)
                    && ReferenceEquals(view.FindName(button.Name), button)) == expectedPopupCount,
                "附加单头资料统一为一个默认关闭的弹层和一个入口");
            check(grid.ActualHeight > view.ActualHeight * 0.35,
                "默认布局给明细保留充足高度，采购卡片也实际拉伸：明细=" + grid.ActualHeight + "，页面=" + view.ActualHeight + "，单头=" + header.ActualHeight);
            var defaultHeight = grid.ActualHeight;
            foreach (var popup in popups)
            {
                var entry = ((SwiftExport.Client.VSTO.Resources.Controls.ImportHeaderPopup)popup).EntryButton
                    ?? (ToggleButton)popup.PlacementTarget;
                check(ReferenceEquals(popup.PlacementTarget, header) && popup.StaysOpen
                    && ReferenceEquals(entry, view.FindName("OtherToggleButton")),
                    "定位容器与开关按钮独立，单头弹层显式驻留");
                entry.SetCurrentValue(ToggleButton.IsCheckedProperty, true); Pump(); view.UpdateLayout();
                check(popup.IsOpen && ReferenceEquals(popup.DataContext, view.DataContext)
                    && Math.Abs(grid.ActualHeight - defaultHeight) < 1,
                    "入口打开真实 Popup，共用编辑模型且不挤占明细：" + entry.Name);
                header.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent }); Pump();
                check(popup.IsOpen && entry.IsChecked == true, "定位容器点击不再冒充入口关闭弹层");
                check(((FrameworkElement)popup.Child).ActualWidth > 0
                    && ((FrameworkElement)popup.Child).ActualHeight > 0,
                    "弹层形成可编辑窗口");
                var editors = Descendants<TextBox>(popup.Child).Where(editor => editor.IsEnabled).ToArray();
                check(editors.All(editor => ReferenceEquals(editor.DataContext, view.DataContext)
                    && editor.FontFamily.Equals(entry.FontFamily)), "独立弹层中的编辑器继续使用页面模型和统一字体");
                var combo = Descendants<ComboBox>(popup.Child).FirstOrDefault(editor => editor.IsEnabled);
                if (combo != null)
                {
                    combo.IsDropDownOpen = true; Pump();
                    check(popup.IsOpen && combo.IsDropDownOpen, "单头弹层内的下拉框展开不关闭外层弹层");
                    combo.IsDropDownOpen = false; Pump();
                }
                var date = Descendants<DatePicker>(popup.Child).FirstOrDefault(editor => editor.IsEnabled);
                if (date != null)
                {
                    date.IsDropDownOpen = true; Pump();
                    check(popup.IsOpen && date.IsDropDownOpen, "单头弹层内的日历展开不关闭外层弹层：" + entry.Name
                        + "，弹层=" + popup.IsOpen + "，日历=" + date.IsDropDownOpen);
                    date.IsDropDownOpen = false; Pump();
                }
                var closeButton = Descendants<Button>(popup.Child).Single(button => ReferenceEquals(button.Command, ApplicationCommands.Close));
                ApplicationCommands.Close.Execute(closeButton.CommandParameter, closeButton); Pump();
                check(!popup.IsOpen && entry.IsChecked == false, "关闭按钮命令同步复位入口");
                entry.SetCurrentValue(ToggleButton.IsCheckedProperty, true); Pump();
                var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(popup.Child), 0, Key.Escape)
                    { RoutedEvent = Keyboard.KeyDownEvent };
                popup.Child.RaiseEvent(escape); Pump();
                check(!popup.IsOpen && entry.IsChecked == false && escape.Handled, "Esc 关闭当前弹层并同步入口");
                entry.SetCurrentValue(ToggleButton.IsCheckedProperty, true); Pump();
                var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent };
                entry.RaiseEvent(click); Pump();
                check(!popup.IsOpen && entry.IsChecked == false && click.Handled,
                    "再次按入口只关闭，避免 ToggleButton 在关闭后立即重开");
            }
            check(Math.Abs(grid.ActualHeight - defaultHeight) < 1 && ReferenceEquals(items, grid.ItemsSource),
                "弹层反复打开关闭不改变明细布局或替换行集合");
        }

        /// <summary>等待当前 STA 的绑定和布局消息完成，不使用后台线程触碰 UI。</summary>
        private static void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        /// <summary>弹层属于独立可视树，必须从 Child 查找实际编辑器。</summary>
        private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T value) yield return value;
                foreach (var nested in Descendants<T>(child)) yield return nested;
            }
        }

    }
}
