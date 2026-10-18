using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExportClient.Features.Customers.CustomerPoConfigs.Models;

namespace SwiftExportClient.Features.Customers.CustomerPoConfigs.ViewModels;

/// <summary>配置页的筛选快照和权限边界；已应用条件拥有分页范围，缓存展示不改变业务资料。</summary>
public sealed partial class CustomerPoConfigManagerViewModel
{
    private QueryFilters _appliedFilters = new(null, null, string.Empty);

    /// <summary>待查询的客户条件，空值不筛选；修改本属性不自动丢弃列表草稿。</summary>
    [ObservableProperty] public partial CustomerDto? FilterCustomer { get; set; }
    /// <summary>待查询的供应商条件，与客户和关键词按 AND 组合。</summary>
    [ObservableProperty] public partial SupplierDto? FilterSupplier { get; set; }

    /// <summary>页面查看权限由真实当前用户提供，不由默认 CanWrite 推断。</summary>
    public bool CanView => !_disposed && _user.HasPermission(PermissionKeys.CustomersPoConfigView);
    /// <summary>仅允许创建新增配置行。</summary>
    public bool CanCreate => CanView && _user.HasPermission(PermissionKeys.CustomersPoConfigCreate);
    /// <summary>仅允许修改已有配置行。</summary>
    public bool CanEdit => CanView && _user.HasPermission(PermissionKeys.CustomersPoConfigEdit);
    /// <summary>仅允许删除已有配置行；移除未保存行使用创建权限。</summary>
    public bool CanDeleteRecords => CanView && _user.HasPermission(PermissionKeys.CustomersPoConfigDelete);
    /// <summary>无创建或编辑权限时文本列只读，勾选仍属于查看操作。</summary>
    public bool IsGridReadOnly => !IsInteractionEnabled || !CanWrite || !(CanCreate || CanEdit);

    /// <summary>原生编辑与选择器、填充共同复核行归属及新增或既有行对应权限。</summary>
    public bool CanEditRow(CustomerPoConfigLineModel? row) => row is not null && IsInteractionEnabled && CanWrite
        && !row.IsDeleted && !row.IsReadOnly && Lines.Items.Contains(row) && (row.IsNew ? CanCreate : CanEdit);

    private bool CanQuery() => CanView && IsNotBusy && !IsSelectionPrompt;
    private bool CanAddRow() => IsInteractionEnabled && CanWrite && CanCreate;
    private CustomerPoConfigLineModel[] DeleteTargets() => Lines.CheckedCount > 0 ? Lines.CheckedItems.ToArray()
        : SelectedLine is null ? [] : [SelectedLine];
    /// <summary>混合选区必须全部具备相应删除或移除权限，不能静默跳过部分行。</summary>
    private bool CanDeleteSelected() => !IsCellSelectionMode && IsInteractionEnabled && CanWrite
        && DeleteTargets() is { Length: > 0 } rows
        && rows.All(row => !row.IsDeleted && Lines.Items.Contains(row) && (row.IsNew ? CanCreate : CanDeleteRecords));

    /// <summary>保存前按真实变更类别复核；允许先提交尚未失焦的原生编辑缓冲。</summary>
    private bool CanSave() => IsInteractionEnabled && CanWrite && (CanCreate || CanEdit || CanDeleteRecords) && AreChangesAuthorized();
    /// <summary>用于写入 Busy 作用域内的二次复核，不把本次写入自己的忙碌状态误判为撤权。</summary>
    private bool AreChangesAuthorized() => CanView && CanWrite
        && (Lines.AddedItems.Count == 0 || CanCreate)
        && (Lines.ModifiedItems.Count == 0 || CanEdit)
        && (Lines.DeletedItems.Count == 0 || CanDeleteRecords);

    /// <summary>清空客户、供应商和关键词后统一查询，取消或失败时恢复成功条件。</summary>
    [RelayCommand(CanExecute = nameof(CanQuery))]
    private Task ClearFiltersAsync() => ApplyFiltersAsync(new QueryFilters(null, null, string.Empty));

    /// <summary>冻结用户提交的三个条件；未保存确认取消及查询失败不改变已应用分页归属。</summary>
    private async Task ApplyFiltersAsync(QueryFilters filters)
    {
        if (!CanQuery()) return;
        if (!await EnsureCanDiscardChangesAsync()) { RestoreFilterEditors(); return; }
        if (!await LoadPageAsync(1, PageSize, filters) && !IsQuerying) RestoreFilterEditors();
    }

    private QueryFilters CaptureFilters() => new(FilterCustomer, FilterSupplier, Keyword);

    /// <summary>恢复最后成功条件；缓存替换后按稳定身份取新对象，不能改成同编码的其他身份。</summary>
    private void RestoreFilterEditors()
    {
        FilterCustomer = _appliedFilters.Customer is { } customer ? _cache.Customers.FirstOrDefault(item => item.Uid == customer.Uid) ?? customer : null;
        FilterSupplier = _appliedFilters.Supplier is { } supplier ? _cache.Suppliers.FirstOrDefault(item => item.Uid == supplier.Uid) ?? supplier : null;
        Keyword = _appliedFilters.Keyword;
    }

    /// <summary>运行时身份或权限变化使旧查询失效，保留当前草稿并同步所有入口资格。</summary>
    private void OnPermissionsChanged(object? sender, EventArgs e)
    {
        Interlocked.Increment(ref _querySequence);
        _queryCts?.Cancel();
        IsQuerying = false;
        NotifyPermissionsAndCommands();
    }

    /// <summary>集中更新显示与操作资格；命令执行方法仍再次验证权限。</summary>
    private void NotifyPermissionsAndCommands()
    {
        OnPropertyChanged(nameof(CanView)); OnPropertyChanged(nameof(CanCreate)); OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanDeleteRecords)); OnPropertyChanged(nameof(IsInteractionEnabled)); OnPropertyChanged(nameof(IsGridReadOnly));
        foreach (var command in new IRelayCommand[] { SearchCommand, RefreshCommand, ClearFiltersCommand, PageChangeCommand,
                     AddRowCommand, DeleteSelectedCommand, SaveCommand, RejectListChangesCommand, CommitCustomerCommand, ClearCustomerCommand,
                     CommitSupplierCommand, ClearSupplierCommand, FillDownCommand }) command.NotifyCanExecuteChanged();
        RefreshCheckState();
    }

    /// <summary>三个条件形成同一查询身份；只属于本页面，不扩张共享 DTO。</summary>
    private sealed record QueryFilters(CustomerDto? Customer, SupplierDto? Supplier, string Keyword);
}
