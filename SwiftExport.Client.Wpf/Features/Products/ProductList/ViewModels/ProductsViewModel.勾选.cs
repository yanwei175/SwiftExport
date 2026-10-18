using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.Input;
using SwiftExportClient.Features.Products.ProductList.Models;

namespace SwiftExportClient.Features.Products.ProductList.ViewModels;

/// <summary>管理列表的显示行勾选接线；只修改本页 UI 勾选状态，不产生业务草稿。</summary>
public sealed partial class ProductsViewModel
{
    private ICollectionView _checkView = null!;

    /// <summary>物料当前页显示行全部勾选时返回 true，不推断未加载的数据。</summary>
    public bool IsAllVisibleChecked => GetVisibleRows() is { Length: > 0 } rows && rows.All(row => row.IsChecked);

    /// <summary>勾选当前原生整行选区；拒绝其他表、隐藏行及已删除行。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeSelectionChecks))]
    private void CheckSelection(IList? rows) => SetSelectionChecks(rows, true);
    /// <summary>取消当前原生整行选区的勾选。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeSelectionChecks))]
    private void UncheckSelection(IList? rows) => SetSelectionChecks(rows, false);
    /// <summary>反转当前原生整行选区的勾选，不改变选区。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeSelectionChecks))]
    private void InvertSelection(IList? rows) => SetSelectionChecks(rows, null);
    /// <summary>勾选物料当前页显示行，不查询其他页或其他主型号。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeVisibleChecks))]
    private void CheckVisible() => SetChecks(GetVisibleRows(), true);
    /// <summary>反转物料当前页显示行的勾选。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeVisibleChecks))]
    private void InvertVisible() => SetChecks(GetVisibleRows(), null);
    /// <summary>接纳表头明确提交的全选值，范围限定于当前显示行。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeVisibleChecks))]
    private void SetVisibleChecks(bool? value) => SetChecks(GetVisibleRows(), value == true);

    private ProductLineModel[] GetVisibleRows() => _checkView.Cast<object>().OfType<ProductLineModel>().Where(row => !row.IsDeleted).ToArray();
    private bool CanChangeChecks() => !_disposed && CanRun() && HasProductViewPermission();
    private bool CanChangeSelectionChecks() => CanChangeChecks() && !IsCellSelectionMode;
    private bool CanChangeVisibleChecks() => CanChangeChecks() && GetVisibleRows().Length > 0;

    /// <summary>冻结这次原生选区，混合类型或不属于当前显示范围时整体拒绝。</summary>
    private void SetSelectionChecks(IList? selection, bool? value)
    {
        if (!CanChangeSelectionChecks() || selection is not { Count: > 0 }) return;
        var rows = selection.OfType<ProductLineModel>().ToArray();
        if (rows.Length != selection.Count) return;
        SetChecks(rows, value);
    }

    /// <summary>执行入口再次复核交互与归属，合并列表通知，保留业务编辑基线。</summary>
    private void SetChecks(IReadOnlyList<ProductLineModel> rows, bool? value)
    {
        if (!CanChangeChecks() || rows.Count == 0) return;
        var visible = GetVisibleRows().ToHashSet();
        if (!rows.All(visible.Contains)) return;
        using (Products.SuppressListEvents())
            foreach (var row in rows.Distinct()) row.IsChecked = value ?? !row.IsChecked;
    }

    /// <summary>连接实际 ItemsSource 默认视图，视图刷新仅用于范围事实，不新增筛选功能。</summary>
    private void InitializeCheckSelection()
    {
        _checkView = CollectionViewSource.GetDefaultView(Products.Items);
        CollectionChangedEventManager.AddHandler(_checkView, OnCheckViewChanged);
        PropertyChanged += OnCheckPageStateChanged;
        foreach (var command in new IRelayCommand[]
        {
            CheckSelectionCommand, UncheckSelectionCommand, InvertSelectionCommand,
            CheckVisibleCommand, InvertVisibleCommand, SetVisibleChecksCommand
        })
            RegisterBusyAwareCommand(command);
    }

    private void OnCheckViewChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshCheckState();

    /// <summary>模式与交互状态变化后同步菜单资格，执行入口仍独立复核。</summary>
    private void OnCheckPageStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IsBusy) or nameof(CanWrite) or nameof(IsCellSelectionMode))
            RefreshCheckState();
    }

    /// <summary>更新本页表头聚合和勾选命令，不触碰行资料或业务草稿。</summary>
    private void RefreshCheckState()
    {
        OnPropertyChanged(nameof(IsAllVisibleChecked));
        CheckSelectionCommand.NotifyCanExecuteChanged();
        UncheckSelectionCommand.NotifyCanExecuteChanged();
        InvertSelectionCommand.NotifyCanExecuteChanged();
        CheckVisibleCommand.NotifyCanExecuteChanged();
        InvertVisibleCommand.NotifyCanExecuteChanged();
        SetVisibleChecksCommand.NotifyCanExecuteChanged();
    }

    /// <summary>页面释放时对称解除视图和页面状态通知，避免卸载后仍更新菜单。</summary>
    private void DetachCheckSelection()
    {
        PropertyChanged -= OnCheckPageStateChanged;
        CollectionChangedEventManager.RemoveHandler(_checkView, OnCheckViewChanged);
    }
}
