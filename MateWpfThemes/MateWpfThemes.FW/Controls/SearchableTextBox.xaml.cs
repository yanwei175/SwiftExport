using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MateWpfThemes.FW.Controls
{
    /// <summary>
    /// 这是一个TextBox+Popup的组合控件,它需要外部提供一个数据源ItemsSource,当用户输入时会在Popup里显示匹配的数据列表,
    /// 外部可以绑定TextBox的Text属性到ViewModel直接获取到选定的Text,也可以通过SelectedItem和SelectedValue获取到选定的对象和对应的值(如果设置了SelectedValuePath)。
    /// 用户可以通过键盘或鼠标选择,选择后会把对应的文本回写到TextBox并更新SelectedItem和SelectedValue属性。
    /// </summary>
    public partial class SearchableTextBox : UserControl
    {
        private readonly DispatcherTimer _debounceTimer;
        private bool _isSearching;//内部正在搜索中

        // 反射缓存池，保证几千条数据搜索不卡
        private readonly Dictionary<Type, Dictionary<string, PropertyInfo>> _propertyCache = new Dictionary<Type, Dictionary<string, PropertyInfo>>();

        // 中文注释：占位符文本，直接透传到内部 InputTextBox。
        public static readonly DependencyProperty PlaceholderProperty =
            DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(SearchableTextBox),
                new PropertyMetadata(string.Empty));

        public string Placeholder
        {
            get => (string)GetValue(PlaceholderProperty);
            set => SetValue(PlaceholderProperty, value);
        }

        public SearchableTextBox()
        {
            InitializeComponent();

            // 防抖定时器，200毫秒手感最佳
            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _debounceTimer.Tick += DebounceTimer_Tick;

            this.Unloaded += (s, e) => _debounceTimer.Stop();
        }

        #region 依赖属性 (对外暴露绑定的接口)

        // 1. Text 属性 (双向绑定到你的 ViewModel)
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(SearchableTextBox),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChangedCallback));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }
        private static void OnTextChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (SearchableTextBox)d;
            if (control.InputTextBox != null && !control._isSearching)
            {
                var newText = e.NewValue?.ToString() ?? string.Empty;

                // 中文注释：统一通过“静默方式”回写输入框，避免触发 Text Changed 连锁回写。
                control.SetInputTextSilently(newText);
            }
        }

        // 2. ItemsSource 属性 (数据源)
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(SearchableTextBox),
                new PropertyMetadata(null, OnItemsSourceChanged));

        public IEnumerable ItemsSource
        {
            get => (IEnumerable)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        // 3. DisplayMemberPath 属性 (显示哪个字段)
        public static readonly DependencyProperty DisplayMemberPathProperty =
            DependencyProperty.Register(nameof(DisplayMemberPath), typeof(string), typeof(SearchableTextBox),
                new PropertyMetadata(string.Empty));

        public string DisplayMemberPath
        {
            get => (string)GetValue(DisplayMemberPathProperty);
            set => SetValue(DisplayMemberPathProperty, value);
        }

        // 4. FilterMemberPaths 属性 (多条件筛选字段，逗号/分号/竖线分隔)
        public static readonly DependencyProperty FilterMemberPathsProperty =
            DependencyProperty.Register(nameof(FilterMemberPaths), typeof(string), typeof(SearchableTextBox),
                new PropertyMetadata(string.Empty));

        public string FilterMemberPaths
        {
            get => (string)GetValue(FilterMemberPathsProperty);
            set => SetValue(FilterMemberPathsProperty, value);
        }

        // 5. RequireItemMatch 属性（是否必须命中下拉项）
        public static readonly DependencyProperty RequireItemMatchProperty =
            DependencyProperty.Register(nameof(RequireItemMatch), typeof(bool), typeof(SearchableTextBox),
                new PropertyMetadata(false));

        public bool RequireItemMatch
        {
            get => (bool)GetValue(RequireItemMatchProperty);
            set => SetValue(RequireItemMatchProperty, value);
        }

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(SearchableTextBox),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public object SelectedItem
        {
            get => GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        public static readonly DependencyProperty SelectedValuePathProperty =
            DependencyProperty.Register(nameof(SelectedValuePath), typeof(string), typeof(SearchableTextBox),
                new PropertyMetadata(string.Empty));

        public string SelectedValuePath
        {
            get => (string)GetValue(SelectedValuePathProperty);
            set => SetValue(SelectedValuePathProperty, value);
        }

        public static readonly DependencyProperty SelectedValueProperty =
            DependencyProperty.Register(nameof(SelectedValue), typeof(object), typeof(SearchableTextBox),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public object SelectedValue
        {
            get => GetValue(SelectedValueProperty);
            set => SetValue(SelectedValueProperty, value);
        }

        // 内部给 ListBox 绑定的视图属性
        public ICollectionView FilteredView { get; private set; }
        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (SearchableTextBox)d;
            if (e.NewValue != null)
            {
                // ★ 核心修复：绝对不要使用 new CollectionView()！
                // 借助 CollectionViewSource，它会自动为你安全的 ObservableCollection 生成对应的 ListCollectionView。
                var cvs = new CollectionViewSource { Source = e.NewValue };
                control.FilteredView = cvs.View;

                control.FilteredView.Filter = control.FilterItem;

                // 重新绑定 ListBox
                if (control.ResultList != null)
                    control.ResultList.ItemsSource = control.FilteredView;
            }
            else
            {
                control.FilteredView = null;
                if (control.ResultList != null)
                    control.ResultList.ItemsSource = null;
            }
        }

        #endregion

        #region 核心过滤逻辑 (防抖 + 缓存)

        private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isSearching) return;

            var newText = InputTextBox.Text ?? string.Empty;

            // 中文注释：统一通过“静默方式”同步到依赖属性，内部自动做重复值短路。
            SyncTextDependencySilently(newText);

            // 只要用户在打字，重新计时防抖
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private void DebounceTimer_Tick(object sender, EventArgs e)
        {
            _debounceTimer.Stop();

            ResolveSelectionByText();

            if (FilteredView != null)
            {
                FilteredView.Refresh();

                if (string.IsNullOrWhiteSpace(InputTextBox.Text))
                {

                    DropdownPopup.IsOpen = true;
                    return;
                }
                else if (!FilteredView.IsEmpty)
                {
                    DropdownPopup.IsOpen = true;
                }
                else { DropdownPopup.IsOpen = false; }

            }
        }

        private bool FilterItem(object item)
        {
            if (string.IsNullOrWhiteSpace(InputTextBox.Text)) return true;
            if (item == null) return false;

            var filterPaths = ParseResolveMemberPaths();
            foreach (var path in filterPaths)
            {
                var value = GetMemberText(item, path);
                if (!string.IsNullOrEmpty(value) && value.IndexOf(InputTextBox.Text, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private PropertyInfo GetCachedProperty(Type type, string propertyName)
        {
            if (!_propertyCache.TryGetValue(type, out var typeProps))
            {
                typeProps = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
                _propertyCache[type] = typeProps;
            }

            if (!typeProps.TryGetValue(propertyName, out var propInfo))
            {
                propInfo = type.GetProperty(propertyName, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
                typeProps[propertyName] = propInfo;
            }

            return propInfo;
        }

        #endregion

        #region 交互控制 (键盘导航与鼠标选择)

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            // 中文注释：清空时使用静默同步，避免触发双向绑定抖动。
            SetInputTextSilently(string.Empty);
            SyncTextDependencySilently(string.Empty);

            // 中文注释：清空时同步清理选中项和值，保证控件状态一致。
            SelectedItem = null;
            SelectedValue = null;

            if (FilteredView != null) FilteredView.Refresh();
            DropdownPopup.IsOpen = true;
            // 2. 确保焦点乖乖回到输入框，方便用户马上开始新的打字
            InputTextBox.Focus();
        }

        private void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space && string.IsNullOrWhiteSpace(InputTextBox.Text))
            {
                FilteredView?.Refresh(); // 确保视图是最新的
                if (!FilteredView.IsEmpty) { ResultList.SelectedIndex = 0; }
                DropdownPopup.IsOpen = true;
                e.Handled = true; // 拦截这个空格，不让它输入到文本框里
                return;
            }

            if (!DropdownPopup.IsOpen || ResultList.Items.Count == 0) return;

            if (e.Key == Key.Down)
            {
                if (ResultList.SelectedIndex < ResultList.Items.Count - 1)
                {
                    ResultList.SelectedIndex++;
                    ResultList.ScrollIntoView(ResultList.SelectedItem);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                if (ResultList.SelectedIndex > 0)
                {
                    ResultList.SelectedIndex--;
                    ResultList.ScrollIntoView(ResultList.SelectedItem);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                CommitSelection(ResultList.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                DropdownPopup.IsOpen = false;
                e.Handled = true;
            }
        }

        private void InputTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!RequireItemMatch) return;

            var hasMatch = ResolveSelectionByText();
            var currentText = (InputTextBox.Text ?? string.Empty).Trim();

            if (!hasMatch && !string.IsNullOrWhiteSpace(currentText))
            {
                // 中文注释：未命中且要求必须匹配时，统一静默清空文本与选中状态。
                SetInputTextSilently(string.Empty);
                SyncTextDependencySilently(string.Empty);
                SelectedItem = null;
                SelectedValue = null;
            }
        }

        private void ResultList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!(e.OriginalSource is DependencyObject originalSource)) return;

            // 越过不可聚焦的 ListBoxItem，直接抓取数据
            var clickedItem = FindAncestor<ListBoxItem>(originalSource);
            if (clickedItem != null)
            {
                CommitSelection(clickedItem.DataContext);
                e.Handled = true;
            }
        }

        private void CommitSelection(object item)
        {
            if (item == null) return;

            string value;
            if (!string.IsNullOrEmpty(DisplayMemberPath))
            {
                var prop = GetCachedProperty(item.GetType(), DisplayMemberPath);
                value = prop?.GetValue(item)?.ToString();
            }
            else
            {
                value = item.ToString();
            }

            if (value != null)
            {
                // 中文注释：先写选中项，再静默同步文本，避免事件回环。
                SelectedItem = item;
                SelectedValue = GetSelectedValue(item);

                SetInputTextSilently(value);
                SyncTextDependencySilently(value);
                InputTextBox.CaretIndex = value.Length;

                DropdownPopup.IsOpen = false;
            }
        }

        private bool ResolveSelectionByText()
        {
            var text = (InputTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text) || ItemsSource == null)
            {
                SelectedItem = null;
                SelectedValue = null;
                return string.IsNullOrWhiteSpace(text);
            }

            var filterPaths = ParseFilterMemberPaths();

            foreach (var item in ItemsSource)
            {
                if (item == null) continue;

                foreach (var path in filterPaths)
                {
                    var value = GetMemberText(item, path);

                    if (string.Equals((value ?? string.Empty).Trim(), text, StringComparison.OrdinalIgnoreCase))
                    {
                        SelectedItem = item;
                        SelectedValue = GetSelectedValue(item);
                        return true;
                    }
                }
            }

            SelectedItem = null;
            SelectedValue = null;
            return false;
        }

        private object GetSelectedValue(object item)
        {
            if (item == null) return null;
            if (string.IsNullOrWhiteSpace(SelectedValuePath)) return item;

            var prop = GetCachedProperty(item.GetType(), SelectedValuePath);
            return prop?.GetValue(item);
        }

        private List<string> ParseFilterMemberPaths()
        {
            if (!string.IsNullOrWhiteSpace(FilterMemberPaths))
            {
                var paths = FilterMemberPaths
                    .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (paths.Count > 0) return paths;
            }

            if (!string.IsNullOrWhiteSpace(DisplayMemberPath))
            {
                return new List<string> { DisplayMemberPath };
            }

            return new List<string> { string.Empty };
        }

        private List<string> ParseResolveMemberPaths()
        {
            var paths = ParseFilterMemberPaths();

            if (!string.IsNullOrWhiteSpace(DisplayMemberPath) && !paths.Contains(DisplayMemberPath, StringComparer.OrdinalIgnoreCase))
            {
                paths.Insert(0, DisplayMemberPath);
            }

            return paths;
        }

        private string GetMemberText(object item, string memberPath)
        {
            if (item == null) return string.Empty;

            if (string.IsNullOrWhiteSpace(memberPath))
                return item.ToString() ?? string.Empty;

            var prop = GetCachedProperty(item.GetType(), memberPath);
            return prop?.GetValue(item)?.ToString() ?? string.Empty;
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T ancestor) return ancestor;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        #endregion

        /// <summary>
        /// 中文注释：静默设置输入框文本。
        /// 作用：阻断 InputTextBox.TextChanged -> Text 反向更新链路，避免重复触发。
        /// </summary>
        private void SetInputTextSilently(string text)
        {
            var target = text ?? string.Empty;
            var current = InputTextBox.Text ?? string.Empty;

            // 中文注释：文本一致时直接返回，减少无意义 UI 通知。
            if (string.Equals(current, target, StringComparison.Ordinal))
            {
                return;
            }

            _isSearching = true;
            try
            {
                InputTextBox.Text = target;
            }
            finally
            {
                _isSearching = false;
            }
        }

        /// <summary>
        /// 中文注释：静默同步到依赖属性 Text。
        /// 作用：统一处理双向绑定写回，并做重复值短路，防止回写抖动。
        /// </summary>
        private void SyncTextDependencySilently(string text)
        {
            var target = text ?? string.Empty;
            var current = Text ?? string.Empty;

            // 中文注释：值相同则不触发依赖属性更新，避免 PropertyChanged 风暴。
            if (string.Equals(current, target, StringComparison.Ordinal))
            {
                return;
            }

            _isSearching = true;
            try
            {
                Text = target;
            }
            finally
            {
                _isSearching = false;
            }
        }
    }
}