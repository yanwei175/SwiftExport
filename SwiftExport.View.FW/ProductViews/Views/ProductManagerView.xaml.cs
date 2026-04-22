using SwiftExport.View.FW.ProductViews.ViewModels;
using SwiftExport.View.FW.ProductViews.Models;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using System.Linq;

namespace SwiftExport.View.FW.ProductViews.Views
{
    /// <summary>
    /// ProductManagerView.xaml 的交互逻辑
    /// </summary>
    public partial class ProductManagerView : UserControl
    {
        // 记录折叠前的高度，以便展开时恢复
        // 中文注释：主区域和 BOM 区域分别缓存，避免使用同一个变量导致互相覆盖。
        private GridLength _cachedDetailHeight = new GridLength(4, GridUnitType.Star);
        private GridLength _cachedMasterHeight = new GridLength(6, GridUnitType.Star);

        // 中文注释：确保首次焦点逻辑只执行一次，避免切换可见性时频繁抢焦点影响体验。
        private bool _hasInitializedFirstFocus;

        public ProductManagerView(ProductManagerViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;

            // 中文注释：控件首次加载完成后，自动聚焦到主表模糊搜索框。
            this.Loaded += ProductManagerView_Loaded;
        }

        private void ProductManagerView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_hasInitializedFirstFocus) return;
            _hasInitializedFirstFocus = true;

            FocusMainSearchTextBoxAndUseEnglishMode();
        }

        /// <summary>
        /// 中文注释：聚焦主表搜索框，并关闭 IME（优先英文输入）。
        /// </summary>
        private void FocusMainSearchTextBoxAndUseEnglishMode()
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (MainSearchTextBox == null) return;

                MainSearchTextBox.Focus();
                MainSearchTextBox.CaretIndex = (MainSearchTextBox.Text ?? string.Empty).Length;
            }, DispatcherPriority.ContextIdle);
        }

        // 处理分割线上 ToggleButton 的点击事件
        private void BtnToggleBom_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as ToggleButton;
            if (btn.IsChecked == true)
            {
                // 折叠：保存当前高度
                _cachedDetailHeight = DetailRow.Height;

                // ★ 核心修复：折叠时同步降低最小高度，避免行仍占据布局空间。
                DetailRow.MinHeight = 0;
                DetailRow.Height = new GridLength(0);

                DetailContainer.Visibility = Visibility.Collapsed;
                btn.Content = new TextBlock { Text = "展开BOM区域▲", FontSize = 9, Foreground = System.Windows.Media.Brushes.Gray };
            }
            else
            {
                // 展开：恢复之前的高度和最小高度（与 XAML 里的默认最小高度保持一致）
                DetailRow.MinHeight = 40;
                DetailRow.Height = _cachedDetailHeight;

                DetailContainer.Visibility = Visibility.Visible;
                btn.Content = new TextBlock { Text = "折叠BOM区域▼", FontSize = 9, Foreground = System.Windows.Media.Brushes.Gray };
            }
        }

        private void BtnToggleProduct_Click(object sender, RoutedEventArgs e)
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
                btn.Content = new TextBlock { Text = "展开产品区域▲", FontSize = 9, Foreground = System.Windows.Media.Brushes.Gray };
            }
            else
            {
                // 展开：恢复之前的高度和最小高度（与 XAML 里的默认最小高度保持一致）
                MasterRow.MinHeight = 40;
                MasterRow.Height = _cachedMasterHeight;

                MasterBorder.Visibility = Visibility.Visible;
                btn.Content = new TextBlock { Text = "折叠产品区域▼", FontSize = 9, Foreground = System.Windows.Media.Brushes.Gray };
            }
        }
    }
}