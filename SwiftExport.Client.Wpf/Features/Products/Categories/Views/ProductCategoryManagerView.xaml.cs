using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SwiftExportClient.Features.Products.Categories.Models;
using SwiftExportClient.Features.Products.Categories.ViewModels;

namespace SwiftExportClient.Features.Products.Categories.Views;

public partial class ProductCategoryManagerView : UserControl
{
    public ProductCategoryManagerView()
    {
        InitializeComponent();
    }

    private async void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ProductCategoryManagerViewModel viewModel)
        {
            await viewModel.SelectCurrentCategoryAsync(e.NewValue as ProductCategoryTreeItemModel);
        }
    }

    private void OnTreeViewPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        SelectTreeViewItem(e.OriginalSource as DependencyObject);
    }

    private static TreeViewItem? SelectTreeViewItem(DependencyObject? originalSource)
    {
        var item = FindAncestor<TreeViewItem>(originalSource);
        if (item is null)
        {
            return null;
        }

        item.IsSelected = true;
        item.Focus();
        return item;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current switch
            {
                Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(current),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => LogicalTreeHelper.GetParent(current)
            };
        }

        return null;
    }
}
