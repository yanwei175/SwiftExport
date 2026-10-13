using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SwiftExportClient.Features.Products.Categories.Models;
using SwiftExportClient.Features.Products.Categories.ViewModels;

namespace SwiftExportClient.Features.Products.Categories.Views;

/// <summary>分类视图，仅桥接树选中恢复与分类表单编辑提交。</summary>
public partial class ProductCategoryManagerView : UserControl
{
    private ProductCategoryManagerViewModel? _attachedViewModel;
    /// <summary>初始化分类树、基础资料和快速归类界面。</summary>
    public ProductCategoryManagerView()
    {
        InitializeComponent();
    }

    /// <summary>连接分类表单的原生输入提交，转换失败时阻止刷新、关闭和切换分类。</summary>
    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (DataContext is not ProductCategoryManagerViewModel viewModel) return;
        _attachedViewModel = viewModel;
        viewModel.CommitPendingEdits = CommitPendingEdits;
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (_attachedViewModel is null) return;
        _attachedViewModel.CommitPendingEdits = null;
        _attachedViewModel = null;
    }

    private async void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ProductCategoryManagerViewModel viewModel)
        {
            try
            {
            if (!CommitPendingEdits())
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

    /// <summary>只提交本页四个表单输入；保留无效转换文本并将焦点留在错误字段。</summary>
    public bool CommitPendingEdits()
    {
        TextBox[] inputs = [CategoryNameInput, CategoryFakeNameInput, CategoryCodeInput, CategorySortInput];
        foreach (var input in inputs)
        {
            input.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            if (!Validation.GetHasError(input)) continue;
            input.Focus();
            return false;
        }
        return true;
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
