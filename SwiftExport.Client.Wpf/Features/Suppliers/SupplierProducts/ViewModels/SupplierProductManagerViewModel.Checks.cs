using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.Input;
using SwiftExportClient.Features.Suppliers.SupplierProducts.Models;

namespace SwiftExportClient.Features.Suppliers.SupplierProducts.ViewModels;

/// <summary>供应商产品页的原生选区勾选，仅操作当前页显示行的 UI 勾选状态。</summary>
public sealed partial class SupplierProductManagerViewModel
{
    private ICollectionView _visibleProducts = null!;

    /// <summary>表头只反映当前页显示行，不根据服务端总数推断其他页。</summary>
    public bool IsAllVisibleChecked => GetVisibleProducts() is { Length: > 0 } rows && rows.All(row => row.IsChecked);

    /// <summary>挂接实际表格的默认视图，复用现有 Busy 和集合变化通知。</summary>
    private void InitializeCheckSelection()
    {
        _visibleProducts = CollectionViewSource.GetDefaultView(Products.Items);
        CollectionChangedEventManager.AddHandler(_visibleProducts, OnVisibleProductsChanged);
        foreach (var command in new IRelayCommand[] { CheckSelectionCommand, UncheckSelectionCommand, InvertSelectionCommand,
            CheckVisibleCommand, InvertVisibleCommand, SetVisibleChecksCommand }) RegisterBusyAwareCommand(command);
    }

    /// <summary>仅整行模式接纳右键菜单的当前原生选区。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeCheckedSelection))]
    private void CheckSelection(IList? rows) => SetSelectedChecks(rows, true);
    /// <summary>取消当前整行选区的勾选。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeCheckedSelection))]
    private void UncheckSelection(IList? rows) => SetSelectedChecks(rows, false);
    /// <summary>反转当前整行选区的勾选，不改变行选择。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeCheckedSelection))]
    private void InvertSelection(IList? rows) => SetSelectedChecks(rows, null);
    /// <summary>全选仅勾选当前页显示行，不查询或改动其他页。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeVisibleChecks))]
    private void CheckVisible() => SetCheckedSelection(GetVisibleProducts(), true);
    /// <summary>反选仅作用于当前页显示行。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeVisibleChecks))]
    private void InvertVisible() => SetCheckedSelection(GetVisibleProducts(), null);
    /// <summary>表头明确提交当前页全选值，不直接改整张业务列表。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeVisibleChecks))]
    private void SetVisibleChecks(bool? value) => SetCheckedSelection(GetVisibleProducts(), value == true);

    private SupplierProductLineModel[] GetVisibleProducts() => _visibleProducts.Cast<object>().OfType<SupplierProductLineModel>().Where(row => !row.IsDeleted).ToArray();
    private bool CanChangeCheckedSelection() => CanRun() && !IsCellSelectionMode;
    private bool CanChangeVisibleChecks() => CanRun() && GetVisibleProducts().Length > 0;

    /// <summary>只采集这次选区快照，拒绝混合对象、隐藏行或其他页面的产品。</summary>
    private void SetSelectedChecks(IList? selection, bool? value)
    {
        if (!CanChangeCheckedSelection() || selection is not { Count: > 0 }) return;
        var rows = selection.OfType<SupplierProductLineModel>().ToArray();
        if (rows.Length != selection.Count) return;
        SetCheckedSelection(rows, value);
    }

    /// <summary>合并列表通知，保留单行通知和业务草稿；不持有原生选择集合。</summary>
    private void SetCheckedSelection(IReadOnlyList<SupplierProductLineModel> rows, bool? value)
    {
        if (!CanRun() || rows.Count == 0) return;
        var visible = GetVisibleProducts().ToHashSet();
        if (!rows.All(visible.Contains)) return;
        using (Products.SuppressListEvents())
            foreach (var row in rows.Distinct()) row.IsChecked = value ?? !row.IsChecked;
    }

    private void OnVisibleProductsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshCheckState();
    /// <summary>视图变化及勾选通知刷新表头和菜单，不引入额外筛选能力。</summary>
    private void RefreshCheckState()
    {
        OnPropertyChanged(nameof(IsAllVisibleChecked));
        CheckVisibleCommand.NotifyCanExecuteChanged(); InvertVisibleCommand.NotifyCanExecuteChanged(); SetVisibleChecksCommand.NotifyCanExecuteChanged();
        CheckSelectionCommand.NotifyCanExecuteChanged(); UncheckSelectionCommand.NotifyCanExecuteChanged(); InvertSelectionCommand.NotifyCanExecuteChanged();
    }

    /// <summary>与页面释放对称解除视图事件，不影响共享供应商候选。</summary>
    private void DetachCheckSelection() => CollectionChangedEventManager.RemoveHandler(_visibleProducts, OnVisibleProductsChanged);
}
