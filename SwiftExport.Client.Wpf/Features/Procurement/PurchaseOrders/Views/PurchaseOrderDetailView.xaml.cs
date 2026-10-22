using System.Windows;
using System.Windows.Controls;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Views;

/// <summary>采购详情视图，负责价格编辑事务与 ViewModel 的生命周期桥接，不承担业务校验或 API 调用。</summary>
public partial class PurchaseOrderDetailView : UserControl
{
    private PurchaseOrderDetailViewModel? _viewModel;

    /// <summary>初始化详情控件，数据上下文仍由文档导航提供。</summary>
    public PurchaseOrderDetailView()
    {
        InitializeComponent();
    }

    /// <summary>挂接当前页面实例的原生编辑提交入口。</summary>
    private void OnLoaded(object sender, RoutedEventArgs e) => AttachViewModel();

    /// <summary>页面卸载时释放回调，避免 ViewModel 保留已卸载的网格。</summary>
    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachViewModel();

    /// <summary>已加载页面切换上下文时对称转移回调所有权。</summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsLoaded) AttachViewModel();
    }

    /// <summary>仅将原生编辑能否提交的结果交给 ViewModel。</summary>
    private void AttachViewModel()
    {
        DetachViewModel();
        _viewModel = DataContext as PurchaseOrderDetailViewModel;
        if (_viewModel is not null) _viewModel.CommitPendingEdits = CommitEdits;
    }

    /// <summary>只清除本视图拥有的回调，不覆盖其他视图后续建立的桥接。</summary>
    private void DetachViewModel()
    {
        if (_viewModel?.CommitPendingEdits == CommitEdits) _viewModel.CommitPendingEdits = null;
        _viewModel = null;
    }

    /// <summary>复用原生 Cell/Row 提交；转换失败时不取消编辑，也不把旧价格当作本次输入发送。</summary>
    private bool CommitEdits() => LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true)
        && LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);
}
