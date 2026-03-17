using System;
using System.Collections;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace SwiftExport.UI.Controls
{
    public partial class SearchableComboBox : UserControl, INotifyPropertyChanged
    {
        private CancellationTokenSource _debounceCts;
        private readonly Dispatcher _dispatcher;

        // ★ 防止回填文字时触发重新搜索的锁
        private bool _isCommittingSelection = false;

        public SearchableComboBox()
        {
            InitializeComponent();
            _dispatcher = Dispatcher.CurrentDispatcher;
        }

        // ================== 1. 依赖属性 (外部免修改) ==================
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register("ItemsSource", typeof(IEnumerable), typeof(SearchableComboBox),
                new PropertyMetadata(null, OnItemsSourceChanged));

        public IEnumerable ItemsSource
        {
            get => (IEnumerable)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (SearchableComboBox)d;
            if (e.NewValue != null)
            {
                control.FilteredView = CollectionViewSource.GetDefaultView(e.NewValue);
            }
        }

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register("SelectedItem", typeof(object), typeof(SearchableComboBox),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public object SelectedItem
        {
            get => GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        public static readonly DependencyProperty SelectionChangedCommandProperty =
            DependencyProperty.Register("SelectionChangedCommand", typeof(ICommand), typeof(SearchableComboBox));

        public ICommand SelectionChangedCommand
        {
            get => (ICommand)GetValue(SelectionChangedCommandProperty);
            set => SetValue(SelectionChangedCommandProperty, value);
        }

        // ================== 2. 内部数据和防抖 ==================
        private ICollectionView _filteredView;
        public ICollectionView FilteredView
        {
            get => _filteredView;
            set { _filteredView = value; OnPropertyChanged(nameof(FilteredView)); }
        }

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged(nameof(SearchText));

                    // 如果不是程序自动回填的，说明是用户在手动打字/删除
                    if (!_isCommittingSelection)
                    {
                        // ★ 关键修复 1：只要用户手动改了字，说明之前的选中项作废了
                        if (SelectedItem != null)
                        {
                            SelectedItem = null;

                            // 如果希望：只要用户删光了文字，或者改了文字，
                            // 就立刻通知外部 ViewModel 去刷新（比如清空右侧列表），
                            // 就把下面这行取消注释：
                            // TriggerCommand(); 
                        }

                        DebounceFilter(_searchText);
                    }
                }
            }
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
                    if (FilteredView == null) return;

                    FilteredView.Filter = (obj) =>
                    {
                        if (string.IsNullOrEmpty(text)) return true;
                        return obj != null && obj.ToString().IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
                    };

                    FilteredView.Refresh();

                    // ★ 关键修复 2：严谨的下拉框开关逻辑
                    if (string.IsNullOrEmpty(text))
                    {
                        // 情况 A：用户把文字删光了
                        // 通常 ERP 的做法是：删光了就关掉下拉框。
                        DropdownPopup.IsOpen = false;

                        // (如果你希望删光文字时，下拉框能弹出来显示“所有人”，就把上面改为 true)
                    }
                    else if (!ResultListBox.Items.IsEmpty)
                    {
                        // 情况 B：有文字，且搜索出了结果 -> 弹开下拉框
                        DropdownPopup.IsOpen = true;
                    }
                    else
                    {
                        // 情况 C：有文字，但没搜到任何东西 -> 关掉下拉框
                        DropdownPopup.IsOpen = false;
                    }
                });
            });
        }

        // ================== 3. 键位交互 (解耦的精髓) ==================

        // TextBox 里的按键
        private void SearchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 按下键：让焦点跳进 ListBox，方便用户用键盘选
            if (e.Key == Key.Down && DropdownPopup.IsOpen && !ResultListBox.Items.IsEmpty)
            {
                ResultListBox.Focus();
                ResultListBox.SelectedIndex = 0;
                e.Handled = true;
            }
            // 按回车：如果刚好有唯一结果，或者用户想直接触发搜索
            else if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                if (DropdownPopup.IsOpen && ResultListBox.Items.Count > 0)
                {
                    // 默认取列表第一项
                    CommitSelection(ResultListBox.Items[0]);
                }
                else
                {
                    // 如果下拉框没开，直接回车也触发一下命令（如果此时有值的话）
                    TriggerCommand();
                }
                e.Handled = true;
            }
        }

        // ListBox 里的按键
        private void ResultListBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                if (ResultListBox.SelectedItem != null)
                {
                    CommitSelection(ResultListBox.SelectedItem);
                }
                e.Handled = true;
            }
            // 按上键，如果到了第一个，跳回 TextBox
            else if (e.Key == Key.Up && ResultListBox.SelectedIndex == 0)
            {
                SearchTextBox.Focus();
                e.Handled = true;
            }
        }

        // 鼠标点击 ListBox 里的项
        private void ResultListBox_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (ResultListBox.SelectedItem != null)
            {
                CommitSelection(ResultListBox.SelectedItem);
            }
        }

        // ================== 4. 核心确认逻辑 ==================

        // 当用户明确“确认选择”时调用
        private void CommitSelection(object item)
        {
            // 1. 加锁，防止 Text 改变引发二次查询
            _isCommittingSelection = true;

            // 2. 赋值
            SelectedItem = item;
            SearchText = item.ToString(); // 把选中的字填回文本框

            // 3. 收起 UI，焦点还给文本框
            DropdownPopup.IsOpen = false;
            SearchTextBox.Focus();
            SearchTextBox.CaretIndex = SearchTextBox.Text.Length; // 光标移到最后

            // 4. 通知外部
            TriggerCommand();

            // 5. 解锁
            _isCommittingSelection = false;
        }

        private void TriggerCommand()
        {
            if (SelectionChangedCommand != null && SelectionChangedCommand.CanExecute(SelectedItem))
            {
                SelectionChangedCommand.Execute(SelectedItem);
            }
        }

        // INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));


    }
}