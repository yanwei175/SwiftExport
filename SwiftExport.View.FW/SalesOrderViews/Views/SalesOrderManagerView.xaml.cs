using SwiftExport.View.FW.SalesOrderViews.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace SwiftExport.View.FW.SalesOrderViews.Views
{
    /// <summary>
    /// SalesOrderManagerView.xaml 的交互逻辑
    /// 销售订单管理列表视图（UserControl）
    /// </summary>
    public partial class SalesOrderManagerView : UserControl
    {
        // 中文注释：记录主列区当前的高度，以便折叠后展开时恢复。
        private GridLength _cachedMasterHeight = new GridLength(6, GridUnitType.Star);

        // 中文注释：确保首次加载逻辑只执行一次，防止切换页面时频繁触发影响性能。
        private bool _hasInitializedFirstFocus;

        public SalesOrderManagerView()
        {
            InitializeComponent();
        }

        public SalesOrderManagerView(SalesOrderManagerViewModel vm) : this()
        {
            this.DataContext = vm;

            // 中文注释：控件首次加载完成后自动聚焦搜索框，提升键盘操作效率。
            this.Loaded += SalesOrderManagerView_Loaded;
        }

        private void SalesOrderManagerView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_hasInitializedFirstFocus) return;
            _hasInitializedFirstFocus = true;

            FocusMainSearchTextBox();
        }

        /// <summary>
        /// 中文注释：聚焦主搜索文本框。
        /// </summary>
        private void FocusMainSearchTextBox()
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (MainSearchTextBox == null) return;

                MainSearchTextBox.Focus();
                MainSearchTextBox.CaretIndex = (MainSearchTextBox.Text ?? string.Empty).Length;
            }, DispatcherPriority.ContextIdle);
        }
    }
}