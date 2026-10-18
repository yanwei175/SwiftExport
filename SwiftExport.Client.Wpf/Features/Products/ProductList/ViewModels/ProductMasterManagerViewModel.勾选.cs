using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.Input;
using SwiftExportClient.Features.Products.ProductList.Models;

namespace SwiftExportClient.Features.Products.ProductList.ViewModels;

/// <summary>管理列表的显示行勾选接线；只修改本页 UI 勾选状态，不产生业务草稿。</summary>
public sealed partial class ProductMasterManagerViewModel
{
    private ICollectionView _masterCheckView = null!;

    /// <summary>主型号当前页显示行全部勾选时返回 true，不推断未加载的数据。</summary>
    public bool IsAllMasterVisibleChecked => GetMasterVisibleRows() is { Length: > 0 } rows && rows.All(row => row.IsChecked);

    /// <summary>勾选当前原生整行选区；拒绝其他表、隐藏行及已删除行。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeMasterSelectionChecks))]
    private void CheckMasterSelection(IList? rows) => SetMasterSelectionChecks(rows, true);
    /// <summary>取消当前原生整行选区的勾选。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeMasterSelectionChecks))]
    private void UncheckMasterSelection(IList? rows) => SetMasterSelectionChecks(rows, false);
    /// <summary>反转当前原生整行选区的勾选，不改变选区。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeMasterSelectionChecks))]
    private void InvertMasterSelection(IList? rows) => SetMasterSelectionChecks(rows, null);
    /// <summary>勾选主型号当前页显示行，不查询其他页或其他主型号。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeMasterVisibleChecks))]
    private void CheckMasterVisible() => SetMasterChecks(GetMasterVisibleRows(), true);
    /// <summary>反转主型号当前页显示行的勾选。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeMasterVisibleChecks))]
    private void InvertMasterVisible() => SetMasterChecks(GetMasterVisibleRows(), null);
    /// <summary>接纳表头明确提交的全选值，范围限定于当前显示行。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeMasterVisibleChecks))]
    private void SetMasterVisibleChecks(bool? value) => SetMasterChecks(GetMasterVisibleRows(), value == true);

    private ProductMasterLineModel[] GetMasterVisibleRows() => _masterCheckView.Cast<object>().OfType<ProductMasterLineModel>().Where(row => !row.IsDeleted).ToArray();
    private bool CanChangeMasterChecks() => !_disposed && IsInteractionEnabled && !IsCardView;
    private bool CanChangeMasterSelectionChecks() => CanChangeMasterChecks() && !IsCellSelectionMode;
    private bool CanChangeMasterVisibleChecks() => CanChangeMasterChecks() && GetMasterVisibleRows().Length > 0;

    /// <summary>冻结这次原生选区，混合类型或不属于当前显示范围时整体拒绝。</summary>
    private void SetMasterSelectionChecks(IList? selection, bool? value)
    {
        if (!CanChangeMasterSelectionChecks() || selection is not { Count: > 0 }) return;
        var rows = selection.OfType<ProductMasterLineModel>().ToArray();
        if (rows.Length != selection.Count) return;
        SetMasterChecks(rows, value);
    }

    /// <summary>执行入口再次复核交互与归属，合并列表通知，保留业务编辑基线。</summary>
    private void SetMasterChecks(IReadOnlyList<ProductMasterLineModel> rows, bool? value)
    {
        if (!CanChangeMasterChecks() || rows.Count == 0) return;
        var visible = GetMasterVisibleRows().ToHashSet();
        if (!rows.All(visible.Contains)) return;
        using (Masters.SuppressListEvents())
            foreach (var row in rows.Distinct()) row.IsChecked = value ?? !row.IsChecked;
    }

    private ICollectionView _neutralCheckView = null!;

    /// <summary>当前主型号关联中性产品显示行全部勾选时返回 true，不推断未加载的数据。</summary>
    public bool IsAllNeutralVisibleChecked => GetNeutralVisibleRows() is { Length: > 0 } rows && rows.All(row => row.IsChecked);

    /// <summary>勾选当前原生整行选区；拒绝其他表、隐藏行及已删除行。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeNeutralSelectionChecks))]
    private void CheckNeutralSelection(IList? rows) => SetNeutralSelectionChecks(rows, true);
    /// <summary>取消当前原生整行选区的勾选。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeNeutralSelectionChecks))]
    private void UncheckNeutralSelection(IList? rows) => SetNeutralSelectionChecks(rows, false);
    /// <summary>反转当前原生整行选区的勾选，不改变选区。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeNeutralSelectionChecks))]
    private void InvertNeutralSelection(IList? rows) => SetNeutralSelectionChecks(rows, null);
    /// <summary>勾选当前主型号关联中性产品显示行，不查询其他页或其他主型号。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeNeutralVisibleChecks))]
    private void CheckNeutralVisible() => SetNeutralChecks(GetNeutralVisibleRows(), true);
    /// <summary>反转当前主型号关联中性产品显示行的勾选。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeNeutralVisibleChecks))]
    private void InvertNeutralVisible() => SetNeutralChecks(GetNeutralVisibleRows(), null);
    /// <summary>接纳表头明确提交的全选值，范围限定于当前显示行。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeNeutralVisibleChecks))]
    private void SetNeutralVisibleChecks(bool? value) => SetNeutralChecks(GetNeutralVisibleRows(), value == true);

    private ProductNeutralLineModel[] GetNeutralVisibleRows() => _neutralCheckView.Cast<object>().OfType<ProductNeutralLineModel>().Where(row => !row.IsDeleted).ToArray();
    private bool CanChangeNeutralChecks() => !_disposed && CanEditNeutrals;
    private bool CanChangeNeutralSelectionChecks() => CanChangeNeutralChecks() && !IsNeutralCellSelectionMode;
    private bool CanChangeNeutralVisibleChecks() => CanChangeNeutralChecks() && GetNeutralVisibleRows().Length > 0;

    /// <summary>冻结这次原生选区，混合类型或不属于当前显示范围时整体拒绝。</summary>
    private void SetNeutralSelectionChecks(IList? selection, bool? value)
    {
        if (!CanChangeNeutralSelectionChecks() || selection is not { Count: > 0 }) return;
        var rows = selection.OfType<ProductNeutralLineModel>().ToArray();
        if (rows.Length != selection.Count) return;
        SetNeutralChecks(rows, value);
    }

    /// <summary>执行入口再次复核交互与归属，合并列表通知，保留业务编辑基线。</summary>
    private void SetNeutralChecks(IReadOnlyList<ProductNeutralLineModel> rows, bool? value)
    {
        if (!CanChangeNeutralChecks() || rows.Count == 0) return;
        var visible = GetNeutralVisibleRows().ToHashSet();
        if (!rows.All(visible.Contains)) return;
        using (Neutrals.SuppressListEvents())
            foreach (var row in rows.Distinct()) row.IsChecked = value ?? !row.IsChecked;
    }

    /// <summary>连接实际 ItemsSource 默认视图，视图刷新仅用于范围事实，不新增筛选功能。</summary>
    private void InitializeCheckSelection()
    {
        _masterCheckView = CollectionViewSource.GetDefaultView(Masters.Items);
        CollectionChangedEventManager.AddHandler(_masterCheckView, OnCheckViewChanged);
        _neutralCheckView = CollectionViewSource.GetDefaultView(Neutrals.Items);
        CollectionChangedEventManager.AddHandler(_neutralCheckView, OnCheckViewChanged);
        PropertyChanged += OnCheckPageStateChanged;
        foreach (var command in new IRelayCommand[]
        {
            CheckMasterSelectionCommand, UncheckMasterSelectionCommand, InvertMasterSelectionCommand,
            CheckMasterVisibleCommand, InvertMasterVisibleCommand, SetMasterVisibleChecksCommand,
            CheckNeutralSelectionCommand, UncheckNeutralSelectionCommand, InvertNeutralSelectionCommand,
            CheckNeutralVisibleCommand, InvertNeutralVisibleCommand, SetNeutralVisibleChecksCommand
        })
            RegisterBusyAwareCommand(command);
    }

    private void OnCheckViewChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshCheckState();

    /// <summary>模式与交互状态变化后同步菜单资格，执行入口仍独立复核。</summary>
    private void OnCheckPageStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IsBusy) or nameof(CanWrite) or nameof(IsQuerying) or nameof(IsSelectionPrompt) or nameof(IsNeutralLoading) or nameof(IsCellSelectionMode) or nameof(IsNeutralCellSelectionMode) or nameof(IsCardView) or nameof(CanEditNeutrals))
            RefreshCheckState();
    }

    /// <summary>更新本页表头聚合和勾选命令，不触碰行资料或业务草稿。</summary>
    private void RefreshCheckState()
    {
        OnPropertyChanged(nameof(IsAllMasterVisibleChecked));
        CheckMasterSelectionCommand.NotifyCanExecuteChanged();
        UncheckMasterSelectionCommand.NotifyCanExecuteChanged();
        InvertMasterSelectionCommand.NotifyCanExecuteChanged();
        CheckMasterVisibleCommand.NotifyCanExecuteChanged();
        InvertMasterVisibleCommand.NotifyCanExecuteChanged();
        SetMasterVisibleChecksCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsAllNeutralVisibleChecked));
        CheckNeutralSelectionCommand.NotifyCanExecuteChanged();
        UncheckNeutralSelectionCommand.NotifyCanExecuteChanged();
        InvertNeutralSelectionCommand.NotifyCanExecuteChanged();
        CheckNeutralVisibleCommand.NotifyCanExecuteChanged();
        InvertNeutralVisibleCommand.NotifyCanExecuteChanged();
        SetNeutralVisibleChecksCommand.NotifyCanExecuteChanged();
    }

    /// <summary>页面释放时对称解除视图和页面状态通知，避免卸载后仍更新菜单。</summary>
    private void DetachCheckSelection()
    {
        PropertyChanged -= OnCheckPageStateChanged;
        CollectionChangedEventManager.RemoveHandler(_masterCheckView, OnCheckViewChanged);
        CollectionChangedEventManager.RemoveHandler(_neutralCheckView, OnCheckViewChanged);
    }
}
