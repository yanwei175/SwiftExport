using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SwiftExport.Client.VSTO.Resources.Controls;

namespace SwiftExport.Client.VSTO.WpfTests
{
    /// <summary>在独立 STA 中验证实际控件、绑定和生命周期，不加载 Excel 或业务 API。</summary>
    internal static class LookupTextBoxChecks
    {
        /// <summary>覆盖筛选、键盘提交、命令、绑定保持、重载和排队焦点失效。</summary>
        internal static void Run(Action<bool, string> check)
        {
            CheckAppearance(check);
            check(LookupTextMatcher.IsMatch("AB something CD", "ab cd"), "空格分段顺序匹配");
            check(!LookupTextMatcher.IsMatch("CD AB", "ab cd"), "匹配保持片段顺序");
            check(LookupTextMatcher.IsMatch(null, " "), "空查询接纳空候选");
            check(!LookupTextMatcher.IsMatch(null, "a"), "非空查询排除空候选");

            var first = new Item { Code = "A001", Name = "Alpha One" };
            var second = new Item { Code = "B002", Name = "Beta Two" };
            var items = new ObservableCollection<Item> { first, second };
            var lookup = Create(items);
            var other = Create(items);
            lookup.Text = "al one";
            lookup.FilteredView.Refresh();
            check(lookup.FilteredView.Cast<object>().Count() == 1, "本地过滤使用多字段及空格通配");
            check(other.FilteredView.Cast<object>().Count() == 2, "相同缓存的两个控件过滤互不影响");
            lookup.Text = "b002";
            lookup.FilteredView.Refresh();
            check(ReferenceEquals(second, lookup.FilteredView.Cast<object>().Single()), "代码字段可匹配候选");

            var state = new SelectionState();
            Bind(lookup, LookupTextBox.SelectedItemProperty, state, "Item");
            Bind(lookup, LookupTextBox.SelectedValueProperty, state, "Value");
            Bind(lookup, LookupTextBox.TextProperty, state, "Text");
            var commits = 0;
            var clears = 0;
            var queries = 0;
            object commandItem = null;
            LookupSelectionCommittedEventArgs committed = null;
            lookup.SelectionCommittedCommand = new Command(p => { commits++; commandItem = p; });
            lookup.SelectionClearedCommand = new Command(p => clears++);
            lookup.QueryCommand = new Command(p => queries++);
            lookup.SelectionCommitted += (s, e) => committed = e;
            var window = Show(lookup);
            try
            {
                var input = (TextBox)lookup.FindName("InputTextBox");
                check(!lookup.IsDropDownOpen, "初始化与绑定不自动展开");
                lookup.SetCurrentValue(LookupTextBox.TextProperty, string.Empty);
                RaiseKey(input, Key.Down);
                check(lookup.IsDropDownOpen, "向下键显式打开候选");
                RaiseKey(input, Key.Down);
                RaiseKey(input, Key.Enter);
                check(ReferenceEquals(second, state.Item) && Equals("B002", state.Value), "键盘提交写回双向绑定");
                check(commits == 1 && ReferenceEquals(commandItem, second), "提交命令只执行一次且默认传候选");
                check(committed != null && committed.CommitReason == LookupCommitReason.Enter, "提交事件保留来源");
                check(!lookup.IsDropDownOpen, "提交关闭弹层");
                check(BindingOperations.IsDataBound(lookup, LookupTextBox.SelectedItemProperty)
                    && BindingOperations.IsDataBound(lookup, LookupTextBox.SelectedValueProperty)
                    && BindingOperations.IsDataBound(lookup, LookupTextBox.TextProperty), "提交不覆盖调用方绑定");
                ((ButtonBase)lookup.FindName("BtnClear")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                check(state.Item == null && state.Value == null && state.Text == string.Empty, "清除写回选择、值和文本");
                check(clears == 1 && commits == 1, "清除仅触发清除命令");
                state.Item = first;
                Pump();
                check(ReferenceEquals(first, lookup.SelectedItem) && commits == 1, "清除后绑定继续接受程序赋值且不提交");
                lookup.ClearSelectionOnEdit = false;
                Invoke(lookup, "PrepareEditorForInput");
                input.Text = "not found";
                RaiseKey(input, Key.Escape);
                check(ReferenceEquals(first, lookup.SelectedItem), "保留模式下 Escape 恢复原选择");
                check(((TextBlock)lookup.FindName("DisplayTextBlock")).Text == first.Name, "取消编辑恢复原显示文字");
                ((ButtonBase)lookup.FindName("BtnClear")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                RaiseKey(input, Key.Space);
                var tab = RaiseKey(input, Key.Tab);
                check(!tab.Handled && lookup.CommitReason == LookupCommitReason.Tab, "Tab 提交但不吞掉宿主焦点导航");
                ((ButtonBase)lookup.FindName("BtnClear")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                RaiseKey(input, Key.Enter);
                check(queries == 1, "无展开候选时 Enter 执行页面查询命令");

                lookup.ClearSelectionOnEdit = true;
                state.Item = second;
                Pump();
                lookup.FocusEditor();
                window.Content = null;
                Pump();
                check(ReferenceEquals(second, lookup.SelectedItem), "卸载后排队焦点不能清掉旧选择");
            }
            finally { window.Close(); Pump(); }
            CheckReloadAndDebounce(items, check);
        }

        /// <summary>验证真实模板、资源、字号和候选滚动布局；各主题重复调用。</summary>
        internal static void CheckAppearance(Action<bool, string> check)
        {
            var lookup = Create(new ObservableCollection<Item> { new Item { Code = "X", Name = "候选" } });
            lookup.FontSize = 17;
            lookup.ShowDropDownButton = true;
            var window = Show(lookup);
            try
            {
                foreach (var key in new[]
                {
                    "ContentControlFontSize", "TextControlForeground", "TextControlBackground",
                    "TextControlElevationBorderBrush", "TextControlBackgroundPointerOver",
                    "TextControlThemePadding", "TextControlBorderThemeThickness", "TextBoxClearButtonHeight",
                    "TextControlButtonForeground", "ControlCornerRadius", "UserTextFillColorSecondaryBrush",
                    "SubtleFillColorSecondaryBrush", "SubtleFillColorTertiaryBrush",
                    "UserLayerOnMicaBaseAltFillColorTertiaryBrush", "UserControlStrokeColorDefaultBrush",
                    "SwiftExportBorderThickness", "SwiftExportDefaltFontSize", "UserLayerFillColorAltBrush"
                })
                    check(lookup.TryFindResource(key) != null, "Lookup 资源存在：" + key);
                var input = (TextBox)lookup.FindName("InputTextBox");
                var results = (ListBox)lookup.FindName("ResultList");
                input.ApplyTemplate();
                check(input.FontSize == 17 && ((TextBlock)lookup.FindName("DisplayTextBlock")).FontSize == 17
                    && ((TextBlock)lookup.FindName("PlaceholderTextBlock")).FontSize == 17, "编辑、显示和占位字号跟随控件");
                check(input.Template.FindName("PART_ContentHost", input) != null, "输入模板具备编辑宿主");
                check(input.Template.FindName("ClearButton", input) == null, "输入模板不再生成重复清除按钮");
                lookup.MatchSuggestionFontSize = true;
                Pump();
                check(results.FontSize == 17, "可选候选字号跟随");
                lookup.MatchSuggestionFontSize = false;
                check(results.FontSize == (double)lookup.FindResource("SwiftExportDefaltFontSize"), "恢复独立候选字号");
                check(ScrollViewer.GetCanContentScroll(results), "候选保留内部滚动");
                lookup.IsBusy = true;
                lookup.IsEnabled = false;
                Pump();
                check(!input.IsEnabled, "宿主禁用状态向输入框传播");
            }
            finally { window.Close(); Pump(); }
        }

        /// <summary>验证重挂载的弱事件、隐藏/卸载时防抖停止及无用户意图不查询。</summary>
        private static void CheckReloadAndDebounce(ObservableCollection<Item> items, Action<bool, string> check)
        {
            var lookup = Create(items);
            var host = new ResultHost();
            lookup.SetCustomResultHost(host);
            var searches = 0;
            lookup.SearchMode = LookupTextBoxSearchMode.Remote;
            lookup.SearchCommand = new Command(p => searches++);
            lookup.Text = "初始化";
            var window = Show(lookup);
            try
            {
                Invoke(lookup, "DebounceTimer_Tick", lookup, EventArgs.Empty);
                check(searches == 0 && !lookup.IsDropDownOpen, "程序初始化及无编辑意图不发远程查询");
                var before = host.RefreshCount;
                items.Add(new Item { Code = "NEW", Name = "新候选" });
                check(host.RefreshCount == before + 1, "当前来源通知刷新自定义候选");
                var timer = (DispatcherTimer)typeof(LookupTextBox).GetField("_debounceTimer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(lookup);
                timer.Start();
                lookup.IsDropDownOpen = true;
                lookup.Visibility = Visibility.Collapsed;
                Pump();
                check(!timer.IsEnabled && !lookup.IsDropDownOpen, "隐藏停止防抖并关闭候选");
                lookup.Visibility = Visibility.Visible;
                Pump();
                window.Content = null;
                Pump();
                before = host.RefreshCount;
                items.Add(new Item { Code = "AFTER", Name = "卸载后" });
                check(host.RefreshCount == before, "卸载后不再接收缓存集合通知");
                window.Content = lookup;
                Pump();
                before = host.RefreshCount;
                items.Add(new Item { Code = "RELOAD", Name = "重载后" });
                check(host.RefreshCount == before + 1, "重载恢复订阅且没有重复通知");
                timer.Start();
                window.Content = null;
                Pump();
                check(!timer.IsEnabled, "卸载停止计时器");
            }
            finally { window.Close(); Pump(); }
        }

        /// <summary>创建只依赖普通候选属性的本地搜索控件。</summary>
        private static LookupTextBox Create(IEnumerable items)
        {
            return new LookupTextBox
            {
                SearchMode = LookupTextBoxSearchMode.Local, ItemsSource = items,
                DisplayMemberPath = "Name", FilterMemberPaths = "Code,Name", SelectedValuePath = "Code"
            };
        }

        /// <summary>用真实 HWND 触发 Loaded/Unloaded；测试窗不激活、不出现在任务栏。</summary>
        private static Window Show(FrameworkElement control)
        {
            var window = new Window
            {
                Content = control, Width = 360, Height = 140, ShowInTaskbar = false,
                ShowActivated = false, Left = -10000, Top = -10000, WindowStyle = WindowStyle.None
            };
            window.Show();
            Pump();
            control.UpdateLayout();
            return window;
        }

        /// <summary>模拟路由键事件，不发送全局键盘输入。</summary>
        private static KeyEventArgs RaiseKey(UIElement input, Key key)
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(input), 0, key)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            input.RaiseEvent(args);
            Pump();
            return args;
        }

        /// <summary>连接真实 WPF 双向绑定，用于检测内部 SetValue 破坏绑定的回归。</summary>
        private static void Bind(DependencyObject target, DependencyProperty property, object source, string path)
        {
            BindingOperations.SetBinding(target, property, new Binding(path)
            { Source = source, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        }

        /// <summary>直接触发编辑/防抖边界，不依赖操作系统前台窗口焦点。</summary>
        private static void Invoke(object instance, string method, params object[] args)
        {
            instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, args);
        }

        /// <summary>推进当前测试线程待处理的绑定及窗口生命周期消息。</summary>
        private static void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        /// <summary>普通业务候选，仅用于验证属性读取。</summary>
        private sealed class Item
        {
            public string Code { get; set; }
            public string Name { get; set; }
        }

        /// <summary>可通知的宿主绑定状态，模拟页面中独立的选择模型。</summary>
        private sealed class SelectionState : DependencyObject
        {
            /// <summary>页面当前选择。</summary>
            public static readonly DependencyProperty ItemProperty = DependencyProperty.Register("Item", typeof(object), typeof(SelectionState));
            /// <summary>页面选择主键。</summary>
            public static readonly DependencyProperty ValueProperty = DependencyProperty.Register("Value", typeof(object), typeof(SelectionState));
            /// <summary>页面搜索输入。</summary>
            public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string), typeof(SelectionState), new PropertyMetadata(string.Empty));
            public object Item { get => GetValue(ItemProperty); set => SetValue(ItemProperty, value); }
            public object Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
            public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
        }

        /// <summary>记录控件交给页面的命令，不运行真实业务。</summary>
        private sealed class Command : ICommand
        {
            private readonly Action<object> execute;
            /// <summary>记录页面命令执行委托。</summary>
            public Command(Action<object> execute) { this.execute = execute; }
            /// <summary>测试命令始终可执行，无状态变更通知。</summary>
            public event EventHandler CanExecuteChanged { add { } remove { } }
            /// <summary>允许控件执行测试命令。</summary>
            public bool CanExecute(object parameter) => true;
            /// <summary>记录控件提交的参数。</summary>
            public void Execute(object parameter) { execute(parameter); }
        }

        /// <summary>观测集合订阅，不实现任何额外业务选择器。</summary>
        private sealed class ResultHost : ILookupResultHost
        {
            public int RefreshCount;
            public bool HasItems => false;
            public object CurrentItem => null;
            /// <summary>记录每次刷新，用于检测重复订阅。</summary>
            public void Refresh(IEnumerable itemsSource, string query) { RefreshCount++; }
            /// <summary>空候选不需要选择首项。</summary>
            public void SelectFirst() { }
            /// <summary>空候选不需要移动选择。</summary>
            public void MoveSelection(int offset) { }
            /// <summary>此测试宿主始终没有精确匹配项。</summary>
            public bool TryResolveExact(string text, out object item) { item = null; return false; }
        }
    }
}
