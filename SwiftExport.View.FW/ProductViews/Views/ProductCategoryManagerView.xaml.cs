using SwiftExport.View.FW.ProductViews.Models;
using SwiftExport.View.FW.ProductViews.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace SwiftExport.View.FW.ProductViews.Views
{
    /// <summary>
    /// ProductCategoryManagerView.xaml 的交互逻辑。
    /// 继承 UserControl 而非 Window，以支持被 DataTemplate 嵌入 ContentControl 路由。
    /// 构造函数改为无参，DataContext 由 WPF DataTemplate 机制自动注入（即 DataTemplate 的 DataType 对应的 ViewModel 实例）。
    /// </summary>
    public partial class ProductCategoryManagerView : UserControl
    {
        // 无参构造：DataTemplate 路由时 WPF 自动 new 此 View，
        // 并将匹配的 ViewModel 实例设为 DataContext，无需手动赋值。
        public ProductCategoryManagerView()
        {
            InitializeComponent();
        }

        private void CategoryTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (this.DataContext is ProductCategoryManagerViewModel vm)
            {
                // 把树上最新点中的节点 (e.NewValue)，赋值给 ViewModel 的 CurrentCategory
                vm.CurrentCategory = e.NewValue as CategoryTreeModel;
            }
        }
    }
}
