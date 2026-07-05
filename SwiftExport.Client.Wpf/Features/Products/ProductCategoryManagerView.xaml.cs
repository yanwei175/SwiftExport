using System.Windows;
using System.Windows.Controls;

namespace SwiftExportClient.Features.Products;

public partial class ProductCategoryManagerView : UserControl
{
    public ProductCategoryManagerView()
    {
        InitializeComponent();
    }

    private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ProductCategoryManagerViewModel viewModel)
        {
            viewModel.CurrentCategory = e.NewValue as ProductCategoryTreeItemModel;
        }
    }
}
