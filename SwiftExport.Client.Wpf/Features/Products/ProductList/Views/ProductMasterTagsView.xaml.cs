using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Resources.Controls.Filter;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SwiftExportClient.Features.Products.ProductList.Views;

/// <summary>主型号动态标签编辑视图，仅桥接公共多选控件弹出层的视觉生命周期。</summary>
public partial class ProductMasterTagsView : UserControl
{
    private readonly Dictionary<MultiSelectLookupButton, Popup> _popups = [];
    private readonly Dictionary<Popup, ProductMasterTagSelectionModel> _openedWorkspaces = [];

    /// <summary>初始化动态标签视图，业务状态沿用父详情 DataContext。</summary>
    public ProductMasterTagsView() => InitializeComponent();

    private void OnMultiSelectLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MultiSelectLookupButton control || _popups.ContainsKey(control)
            || control.FindName("PART_Popup") is not Popup popup) return;
        _popups.Add(control, popup);
        popup.Opened += OnPopupOpened;
        popup.Closed += OnPopupClosed;
    }

    private void OnMultiSelectUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MultiSelectLookupButton control || !_popups.Remove(control, out var popup)) return;
        popup.Opened -= OnPopupOpened;
        popup.Closed -= OnPopupClosed;
        EndPopup(popup);
    }

    private void OnPopupOpened(object? sender, EventArgs e)
    {
        if (sender is not Popup popup || DataContext is not ProductMasterDetailViewModel vm
            || _openedWorkspaces.ContainsKey(popup)) return;
        _openedWorkspaces.Add(popup, vm.Tags);
        vm.Tags.BeginPopupEdit();
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        if (sender is Popup popup) EndPopup(popup);
    }

    /// <summary>对称释放打开状态，切页签卸载与普通关闭都不能留下延迟刷新阻塞。</summary>
    private void EndPopup(Popup popup)
    {
        if (_openedWorkspaces.Remove(popup, out var workspace)) workspace.EndPopupEdit();
    }
}
