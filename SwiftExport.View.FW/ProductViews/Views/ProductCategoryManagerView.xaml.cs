using SwiftExport.View.FW.ProductViews.Models;
using SwiftExport.View.FW.ProductViews.ViewModels;
using System.Windows;

namespace SwiftExport.View.FW.ProductViews.Views
{
    /// <summary>
    /// ProductCategoryManagerView.xaml 的交互逻辑
    /// </summary>
    public partial class ProductCategoryManagerView : Window
    {
        public ProductCategoryManagerView(ProductCategoryManagerViewModel vm)
        {
            InitializeComponent();
            this.DataContext = vm;
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
