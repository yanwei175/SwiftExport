using SwiftExport.View.FW.ProductViews.ViewModels;
using SwiftExport.View.FW.SalesOrderViews.Models;
using SwiftExport.View.FW.SalesOrderViews.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace SwiftExport.View.FW.SalesOrderViews.Views

{    /// <summary>
     /// SalesOrderManagerView.xaml 的交互逻辑
     /// 销售订单管理列表界面
     /// </summary>
    public partial class SalesOrderManagerView : UserControl
    {
        // 记录折叠前的高度，以便展开时恢复
        // 中文注释：主区域和 BOM 区域分别缓存，避免使用同一个变量导致互相覆盖。
        private GridLength _cachedMasterHeight = new GridLength(6, GridUnitType.Star);

        // 中文注释：确保首次焦点逻辑只执行一次，避免切换可见性时频繁抢焦点影响体验。
        private bool _hasInitializedFirstFocus;
        public SalesOrderManagerView()
        {
            InitializeComponent();
        }
        public SalesOrderManagerView(SalesOrderManagerViewModel vm):this()
        {
            
            this.DataContext = vm;

            // 中文注释：控件首次加载完成后，自动聚焦到主表模糊搜索框。
            this.Loaded += SalesOrderManagerView_Loaded;
        }

        private void SalesOrderManagerView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_hasInitializedFirstFocus) return;
            _hasInitializedFirstFocus = true;

            FocusMainSearchTextBox();
        }

        /// <summary>
        /// 中文注释：聚焦主表搜索框。
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

        // 处理分割线上 ToggleButton 的点击事件

        private void BtnToggleMasterBorder_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as ToggleButton;
            if (btn.IsChecked == true)
            {
                // 折叠：保存当前高度
                _cachedMasterHeight = MasterRow.Height;

                // ★ 核心修复：折叠时同步降低最小高度，避免行仍占据布局空间。
                MasterRow.MinHeight = 0;
                MasterRow.Height = new GridLength(0);

                MasterBorder.Visibility = Visibility.Collapsed;
                btn.Content = new TextBlock { Text = "展开▲", FontSize = 9, Foreground = System.Windows.Media.Brushes.Gray };
            }
            else
            {
                // 展开：恢复之前的高度和最小高度（与 XAML 里的默认最小高度保持一致）
                MasterRow.MinHeight = 40;
                MasterRow.Height = _cachedMasterHeight;

                MasterBorder.Visibility = Visibility.Visible;
                btn.Content = new TextBlock { Text = "折叠▼", FontSize = 9, Foreground = System.Windows.Media.Brushes.Gray };
            }
        }
    }


}
