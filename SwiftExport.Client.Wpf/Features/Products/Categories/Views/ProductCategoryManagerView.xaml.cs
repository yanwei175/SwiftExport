using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SwiftExportClient.Features.Products.Categories.Models;
using SwiftExportClient.Features.Products.Categories.ViewModels;

namespace SwiftExportClient.Features.Products.Categories.Views;

/// <summary>分类宿主视图，仅桥接树选中恢复、页签激活与编辑提交。</summary>
public partial class ProductCategoryManagerView : UserControl
{
    private ProductCategoryManagerViewModel? _attachedViewModel;
    /// <summary>初始化分类资料与标签定义页签。</summary>
    public ProductCategoryManagerView()
    {
        InitializeComponent();
    }

    /// <summary>宿主保留两个页签的原生编辑提交桥接，关闭检查也能看见非当前页签的无效输入。</summary>
    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (DataContext is not ProductCategoryManagerViewModel viewModel) return;
        _attachedViewModel = viewModel;
        viewModel.CommitPendingEdits = AttributeDefinitions.CommitPendingEdits;
        viewModel.Attributes.CommitPendingEdits = AttributeDefinitions.CommitPendingEdits;
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (_attachedViewModel is null) return;
        _attachedViewModel.CommitPendingEdits = null;
        _attachedViewModel.Attributes.CommitPendingEdits = null;
        _attachedViewModel = null;
    }

    private async void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ProductCategoryManagerViewModel viewModel)
        {
            try
            {
            if (!AttributeDefinitions.CommitPendingEdits())
            {
                if (e.NewValue is ProductCategoryTreeItemModel attempted) attempted.IsSelected = false;
                if (viewModel.CurrentCategory is { } previous) previous.IsSelected = true;
                return;
            }
            await viewModel.SelectCurrentCategoryAsync(e.NewValue as ProductCategoryTreeItemModel);
            }
            catch (Exception exception)
            {
                viewModel.ReportInteractionFailure("切换分类", exception);
            }
        }
    }

    private async void OnDetailsTabChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!ReferenceEquals(args.OriginalSource, sender)) return;
        if (DataContext is ProductCategoryManagerViewModel viewModel)
        {
            try { await viewModel.ActivateSelectedTabAsync(); }
            catch (Exception exception) { viewModel.ReportInteractionFailure("打开标签定义", exception); }
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
