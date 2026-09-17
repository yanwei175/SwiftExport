using System.Windows.Controls;
using SwiftExportClient.Features.Shipping.PackingLists.Models;
using SwiftExportClient.Features.Shipping.PackingLists.ViewModels;

namespace SwiftExportClient.Features.Shipping.PackingLists.Views;

/// <summary>只桥接原生 Extended 选择与行 IsChecked，不拦截鼠标或改写 Grid 的 Ctrl/Shift 行为。</summary>
public partial class PackingListSourcePickerView : UserControl
{
    private PackingListSourcePickerViewModel? _owner;
    private bool _syncing;
    /// <summary>初始化原生选择视图并管理会话事件生命周期。</summary>
    public PackingListSourcePickerView()
    {
        InitializeComponent();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        DataContextChanged += (_, _) => { Detach(); if (IsLoaded) Attach(); };
    }
    private void Attach()
    {
        Detach();
        _owner = DataContext as PackingListSourcePickerViewModel;
        if (_owner is not null) _owner.StateChanged += OnStateChanged;
        OnStateChanged(this, EventArgs.Empty);
    }
    private void Detach()
    { if (_owner is not null) _owner.StateChanged -= OnStateChanged; _owner = null; }
    /// <summary>原生选择完成后同步业务勾选，避免预览鼠标事件破坏范围选择。</summary>
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            foreach (var row in e.RemovedItems.OfType<PackingListSourcePickerRowModel>()) row.IsChecked = false;
            foreach (var row in e.AddedItems.OfType<PackingListSourcePickerRowModel>()) row.IsChecked = true;
        }
        finally { _syncing = false; }
    }
    /// <summary>清空勾选或替换数据时同步原生选择集合；仅持有数据项，不缓存虚拟化行容器。</summary>
    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (_syncing || _owner is null) return;
        _syncing = true;
        try
        {
            foreach (var row in CandidatesGrid.SelectedItems.OfType<PackingListSourcePickerRowModel>().ToArray())
                if (!row.IsChecked || !_owner.Items.Contains(row)) CandidatesGrid.SelectedItems.Remove(row);
            foreach (var row in _owner.Items.Where(x => x.IsChecked))
                if (CandidatesGrid.Items.Contains(row) && !CandidatesGrid.SelectedItems.Contains(row)) CandidatesGrid.SelectedItems.Add(row);
        }
        finally { _syncing = false; }
    }
}
