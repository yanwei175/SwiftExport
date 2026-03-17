using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace SwiftExport.UI.Controls
{
    public partial class SearchableListEditor : UserControl
    {
        private CancellationTokenSource _debounceCts;
        private readonly Dispatcher _dispatcher;

        // 依赖属性
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register("ItemsSource", typeof(IEnumerable), typeof(SearchableListEditor), new PropertyMetadata(null));

        public IEnumerable ItemsSource
        {
            get => (IEnumerable)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public static readonly DependencyProperty DisplayMemberPathProperty =
            DependencyProperty.Register("DisplayMemberPath", typeof(string), typeof(SearchableListEditor), new PropertyMetadata(string.Empty));

        public string DisplayMemberPath
        {
            get => (string)GetValue(DisplayMemberPathProperty);
            set => SetValue(DisplayMemberPathProperty, value);
        }

        // 核心事件：通知外部已选中
        public event EventHandler<object> ItemSelected;

        public SearchableListEditor()
        {
            InitializeComponent();
            _dispatcher = Dispatcher.CurrentDispatcher;
        }

        // ================== 1. 外部控制接口 ==================
        private bool _isInternalReset = false; // 增加一个内部重置锁
        public void PrepareForNewSearch()
        {
            _isInternalReset = true; // 开启锁，禁止 TextChanged 触发防抖过滤
            SearchBox.Text = string.Empty;

            // 清空搜索文本时，由于 TextBox 的 TextChanged 会触发防抖过滤，
            // 为了防止残影，我们在清空前强制重置一下 Filter
            var view = CollectionViewSource.GetDefaultView(ItemsSource);
            if (view != null)
            {
                using (view.DeferRefresh())
                {
                    view.Filter = null;
                }
            }

            ResultListBox.SelectedItem = null;
            SearchBox.Focus();

            _isInternalReset = false; // 解锁

        }

        // ================== 2. 核心提交逻辑 ==================

        private void CommitSelection(object item)
        {
            if (item == null) return;
            // 抛给外部窗体去处理并关闭 Popup
            ItemSelected?.Invoke(this, item);
        }

        // ================== 3. 键鼠交互 (上下键与精准点击) ==================

        // 文本框里的按键
        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 按下键：焦点跳进 ListBox
            if (e.Key == Key.Down && !ResultListBox.Items.IsEmpty)
            {
                ResultListBox.Focus();
                ResultListBox.SelectedIndex = 0;

                // 把 ListBox 的项滚动到可视区域
                var item = ResultListBox.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
                item?.Focus();

                e.Handled = true;
            }
            // 按回车：默认选中第一项
            else if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                if (!ResultListBox.Items.IsEmpty)
                {
                    CommitSelection(ResultListBox.Items[0]);
                    e.Handled = true;
                }
            }
        }

        // ListBox 里的按键
        private void ResultListBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                CommitSelection(ResultListBox.SelectedItem);
                e.Handled = true;
            }
            // 按上键：如果到了第一个，跳回 TextBox
            else if (e.Key == Key.Up && ResultListBox.SelectedIndex == 0)
            {
                SearchBox.Focus();
                // 把光标放到文字最后面
                SearchBox.CaretIndex = SearchBox.Text.Length;
                e.Handled = true;
            }
        }

        // ★ 鼠标点击（彻底解决滚动条误触和 SelectionChanged 的坑）
        private void ResultListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 精准判定：必须是点到了具体的某一个 Item 上，而不是点在空白处或滚动条上！
            if (ItemsControl.ContainerFromElement(ResultListBox, (DependencyObject)e.OriginalSource) is ListBoxItem itemContainer)
            {
                // 获取真正的数据实体
                var dataItem = ResultListBox.ItemContainerGenerator.ItemFromContainer(itemContainer);
                CommitSelection(dataItem);
                e.Handled = true;
            }
        }

        // ================== 4. 防抖与反射查询 ==================

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInternalReset) return;// 如果是内部重置，直接跳过，防止二次刷新导致闪烁
            DebounceFilter(SearchBox.Text.ToLower());
        }

        private void DebounceFilter(string text)
        {
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;

            Task.Delay(300, token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;

                _dispatcher.Invoke(() =>
                {
                    if (ItemsSource == null) return;
                    var view = CollectionViewSource.GetDefaultView(ItemsSource);
                    if (view == null) return;

                    view.Filter = (obj) =>
                    {
                        if (string.IsNullOrWhiteSpace(text)) return true;
                        if (obj == null) return false;

                        // ★ 反射获取属性值进行比对
                        if (!string.IsNullOrEmpty(DisplayMemberPath))
                        {
                            var prop = obj.GetType().GetProperty(DisplayMemberPath);
                            if (prop != null)
                            {
                                var val = prop.GetValue(obj)?.ToString();
                                return val != null && val.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
                            }
                        }

                        // 兜底方案
                        return obj.ToString().IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
                    };
                    view.Refresh();
                });
            });
        }
    }
}