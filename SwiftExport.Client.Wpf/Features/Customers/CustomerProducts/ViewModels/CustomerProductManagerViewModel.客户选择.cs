using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExportClient.Features.Customers.CustomerManagement.Models;
using SwiftExportClient.Services.Caching;

namespace SwiftExportClient.Features.Customers.CustomerProducts.ViewModels;

/// <summary>同一客户产品页面的客户选择、文档状态和生命周期，不创建内层编辑器对象。</summary>
public sealed partial class CustomerProductManagerViewModel
{
    /// <summary>保持客户产品页既有路由身份。</summary>
    public const string RouteKey = "customer-products";
    private readonly IReferenceDataCacheService _referenceDataCache;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _pageCts, _lookupCts;
    private Task? _initializationTask;
    private bool _disposed;

    /// <summary>选择器直接消费共享只读缓存，不修改缓存 DTO。</summary>
    public ReadOnlyObservableCollection<CustomerDto> CustomerOptions => _referenceDataCache.Customers;
    /// <summary>标题取当前客户身份，与产品查询上下文保持一致。</summary>
    public string DocumentTitle => ActiveCustomer is null ? "客户产品管理" : $"客户产品管理 - {ActiveCustomer.CustomerCode}";
    /// <summary>页签直接消费产品集合的真实草稿。</summary>
    public bool HasUnsavedChanges => HasChanges;
    /// <summary>加载可被新客户替代，写入和确认期间不能切换客户。</summary>
    public bool CanChangeCustomer => !_disposed && IsNotBusy && !IsPrompting;
    /// <summary>仅当前客户加载完成后允许编辑，避免把新客户标题与旧数据混用。</summary>
    public bool CanEditProducts => CanChangeCustomer && IsOpen && !IsLoading;
    /// <summary>采集原生表格尚未提交的输入，无效输入阻止保存和更换上下文。</summary>
    public Func<bool>? CommitPendingEdits { get; set; }
    /// <summary>保存选择器候选，客户切换只在显式命令内进行。</summary>
    [ObservableProperty] public partial CustomerDto? SelectedCustomer { get; set; }
    /// <summary>同一时间只有一个放弃草稿确认 owner。</summary>
    [ObservableProperty] public partial bool IsPrompting { get; set; }

    /// <summary>重复初始化共享一次缓存加载，释放后不再启动。</summary>
    public Task InitializeAsync() => _disposed ? Task.CompletedTask : _initializationTask ??= LoadOptionsAsync();

    /// <summary>缓存接口无取消参数，取消页面等待后不显示过期反馈。</summary>
    private async Task LoadOptionsAsync()
    {
        var token = _lifetime.Token;
        try
        {
            var result = await _referenceDataCache.EnsureLoadedAsync().WaitAsync(token);
            if (!_disposed && !result.Success) _messageService.ShowWarning("基础资料加载失败", result.Message);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    /// <summary>允许新客户替代旧查询；拒绝切换时恢复当前客户的选择显示。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task CommitCustomerAsync()
    {
        var candidate = SelectedCustomer;
        if (!CanChangeCustomer) { SynchronizeSelection(ActiveCustomer); return; }
        if (candidate is null) return;
        var customer = new CustomerLineModel(); customer.Load(candidate);
        await SelectCustomerAsync(customer);
        if (!_disposed) SynchronizeSelection(ActiveCustomer);
    }

    /// <summary>清空选择和工具栏清空共用草稿门禁，取消后恢复客户。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeCustomer))]
    private async Task ClearCustomerAsync()
    {
        if (_disposed) return;
        await CloseForNavigationAsync();
        if (!_disposed) SynchronizeSelection(ActiveCustomer);
    }

    /// <summary>关闭页签前提交原生输入并检查草稿，写入期间保持页面和数据。</summary>
    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanChangeCustomer || !(CommitPendingEdits?.Invoke() ?? true)) return false;
        if (!IsOpen) return true;
        if (!await EnsureCanDiscardChangesAsync(cancellationToken)) return false;
        CloseCore(); return true;
    }

    /// <summary>客户身份变更直接同步唯一页面的标题和选择器。</summary>
    partial void OnActiveCustomerChanged(CustomerLineModel? value)
    {
        Title = DocumentTitle;
        SynchronizeSelection(value);
    }

    /// <summary>客户缓存已移除该客户时仍以当前快照回显身份。</summary>
    private void SynchronizeSelection(CustomerLineModel? customer) => SelectedCustomer = customer is null ? null
        : CustomerOptions.FirstOrDefault(item => item.Uid == customer.Uid) ?? customer.ToDto();

    /// <summary>查询、确认和写入状态变化刷新页面能力，不再订阅第二个 VM。</summary>
    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IsBusy) or nameof(IsOpen) or nameof(IsLoading) or nameof(IsPrompting)) NotifyCapabilities();
    }

    /// <summary>对称取消读取、使旧响应失效并解除集合订阅，不取消已发出的保存。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel();
        Interlocked.Increment(ref _loadVersion); Interlocked.Increment(ref _lookupVersion);
        CancelRead(ref _pageCts); CancelRead(ref _lookupCts);
        Products.PropertyChanged -= OnListPropertyChanged; PropertyChanged -= OnPagePropertyChanged;
        DetachCheckSelection();
        CommitPendingEdits = null; _lifetime.Dispose();
    }

    /// <summary>仅置换当前页面拥有的读取，底层接口不支持 Token 时由等待和序号共同保护。</summary>
    private static void CancelRead(ref CancellationTokenSource? source)
    {
        var previous = Interlocked.Exchange(ref source, null); previous?.Cancel(); previous?.Dispose();
    }
}
