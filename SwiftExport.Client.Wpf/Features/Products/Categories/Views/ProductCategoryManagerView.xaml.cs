using System.Windows;
using System.Windows.Controls;
using SwiftExportClient.Features.Products.Categories.Models;
using SwiftExportClient.Features.Products.Categories.ViewModels;

namespace SwiftExportClient.Features.Products.Categories.Views;

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
