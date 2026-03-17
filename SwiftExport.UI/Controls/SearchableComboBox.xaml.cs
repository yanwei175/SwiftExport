using System.Collections;
using System.ComponentModel;
using System.Windows.Forms;
using System.Windows.Input;

namespace SwiftExport.UI.Controls
{
    public partial class SearchableComboBox : UserControl, INotifyPropertyChanged
    {
        public SearchableComboBox()
        {
            InitializeComponent();
        }

        // ================== 1. ItemsSource (输入数据源) ==================
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register("ItemsSource", typeof(IEnumerable), typeof(SearchableComboBox),
                new PropertyMetadata(null, OnItemsSourceChanged));

        public IEnumerable ItemsSource
        {
            get => (IEnumerable)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        // 当父窗体传进来新数据时，我们初始化内部的 CollectionView
        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (SearchableComboBox)d;
            if (e.NewValue != null)
            {
                control.FilteredView = CollectionViewSource.GetDefaultView(e.NewValue);
            }
        }

        // ================== 2. SelectedItem (双向绑定结果) ==================
        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register("SelectedItem", typeof(object), typeof(SearchableComboBox),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public object SelectedItem
        {
            get => GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        // ================== 3. SelectionChangedCommand (通知父窗体的命令) ==================
        // ★ 这就是你想要的：告诉外部“我选完了，你可以干活了”
        public static readonly DependencyProperty SelectionChangedCommandProperty =
            DependencyProperty.Register("SelectionChangedCommand", typeof(ICommand), typeof(SearchableComboBox));

        public ICommand SelectionChangedCommand
        {
            get => (ICommand)GetValue(SelectionChangedCommandProperty);
            set => SetValue(SelectionChangedCommandProperty, value);
        }

        // ================== 4. 内部逻辑 (过滤、视图) ==================

        // 这是内部使用的视图，不对外暴露
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
                _searchText = value;
                OnPropertyChanged(nameof(SearchText));
                FilterData(_searchText); // 这里可以加 Task.Delay 防抖
            }
        }

        // 简单的过滤逻辑 (你可以把防抖加在这里)
        private void FilterData(string text)
        {
            if (FilteredView == null) return;
            FilteredView.Filter = (obj) =>
            {
                if (string.IsNullOrEmpty(text)) return true;
                return obj.ToString().IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
            };
            FilteredView.Refresh();

            // 自动展开
            if (!string.IsNullOrEmpty(text) && !MainComboBox.IsDropDownOpen)
            {
                MainComboBox.IsDropDownOpen = true;
            }
        }

        // 键位处理 (解决卡顿和方向键问题)
        private void MainComboBox_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down || e.Key == Key.Up || e.Key == Key.Enter) return;
            // 可以在这里触发 FilterData
        }

        // ================== 5. 触发命令的时机 ==================

        // 当下拉框关闭（用户确认选择）时，触发命令
        private void MainComboBox_DropDownClosed(object sender, EventArgs e)
        {
            // 如果绑定了命令，并且命令可以执行
            if (SelectionChangedCommand != null && SelectionChangedCommand.CanExecute(SelectedItem))
            {
                // 执行命令！可以把选中的项作为参数传过去
                SelectionChangedCommand.Execute(SelectedItem);
            }
        }

        // INotifyPropertyChanged 实现...
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}