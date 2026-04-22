using SwiftExport.View.FW.ProductViews.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SwiftExport.View.FW.ProductViews.Views
{
    /// <summary>
    /// ProductManagerView.xaml 的交互逻辑
    /// </summary>
    public partial class MainProductManagerView : UserControl
    {
        // 记录折叠前的高度，以便展开时恢复
        private GridLength _cachedDetailHeight = new GridLength(4, GridUnitType.Star);
        public MainProductManagerView( )
        {
            InitializeComponent();
        }
        public MainProductManagerView(MainProductManagerViewModel vm):this()
        {
            this.DataContext = vm;
        }

        // 处理分割线上 ToggleButton 的点击事件
        private void BtnToggleBom_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as ToggleButton;
            if (btn.IsChecked == true)
            {
                // 折叠：保存当前高度
                _cachedDetailHeight = DetailRow.Height;

                // ★ 核心修复：必须把 MinHeight 也干掉，否则 Grid 依然会强行霸占 200 像素！
                DetailRow.MinHeight = 0;
                DetailRow.Height = new GridLength(0);

                DetailContainer.Visibility = Visibility.Collapsed;
                btn.Content = new TextBlock { Text = "展开BOM区域▲", FontSize = 9, Foreground = System.Windows.Media.Brushes.Gray };
            }
            else
            {
                // 展开：恢复之前的高度和 MinHeight
                DetailRow.MinHeight = 200; // ★ 恢复保底高度
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
                _cachedDetailHeight = MasterRow.Height;

                // ★ 核心修复：必须把 MinHeight 也干掉，否则 Grid 依然会强行霸占 200 像素！
                MasterBorder.MinHeight = 0;
                MasterRow.Height = new GridLength(0);

                MasterBorder.Visibility = Visibility.Collapsed;
                btn.Content = new TextBlock { Text = "展开产品区域▲", FontSize = 9, Foreground = System.Windows.Media.Brushes.Gray };
            }
            else
            {
                // 展开：恢复之前的高度和 MinHeight
                MasterDataGrid.MinHeight = 200; // ★ 恢复保底高度
                MasterRow.Height = _cachedDetailHeight;

                MasterBorder.Visibility = Visibility.Visible;
                btn.Content = new TextBlock { Text = "折叠产品区域▼", FontSize = 9, Foreground = System.Windows.Media.Brushes.Gray };
            }
        }
    }
}
