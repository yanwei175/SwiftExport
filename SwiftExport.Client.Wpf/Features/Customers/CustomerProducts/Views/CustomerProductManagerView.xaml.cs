using System.Windows.Controls;
using System.Windows;
using SwiftExportClient.Features.Customers.CustomerProducts.ViewModels;

namespace SwiftExportClient.Features.Customers.CustomerProducts.Views;

/// <summary>统一客户产品页面，只桥接原生编辑提交；导航页签拥有 VM 释放。</summary>
public partial class CustomerProductManagerView : UserControl
{
    private CustomerProductManagerViewModel? _model;
    /// <summary>初始化客户选择与客户产品表格。</summary>
    public CustomerProductManagerView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e) => Attach();
    private void OnUnloaded(object sender, RoutedEventArgs e) => Detach();
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) { if (IsLoaded) Attach(); }
    private void Attach()
    {
        Detach(); _model = DataContext as CustomerProductManagerViewModel;
        if (_model is not null) _model.CommitPendingEdits = CommitEdits;
    }
    /// <summary>卸载或替换上下文时解除页面回调，不遗留对视觉树的引用。</summary>
    private void Detach()
    {
        if (_model?.CommitPendingEdits == CommitEdits) _model.CommitPendingEdits = null;
        _model = null;
    }
    private bool CommitEdits() => ProductGrid.CommitEdit(DataGridEditingUnit.Cell, true) && ProductGrid.CommitEdit(DataGridEditingUnit.Row, true);
}
