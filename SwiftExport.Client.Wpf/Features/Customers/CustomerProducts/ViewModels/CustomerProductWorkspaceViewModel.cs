using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExportClient.Features.Customers.CustomerManagement.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Customers.CustomerProducts.ViewModels;

/// <summary>从会话缓存选择客户，协调产品编辑器切换及未保存变更保护。</summary>
public sealed partial class CustomerProductWorkspaceViewModel
    : ViewModelBaseEx, IDocumentTabViewModel, IAsyncInitializable, IDisposable
{
    /// <summary>标识独立客户产品工作区的导航路由。</summary>
    public const string RouteKey = "customer-products";
    private readonly IReferenceDataCacheService _referenceDataCache;
    private readonly IMessageService _messageService;
    private Task? _initializationTask;
    private bool _disposed;

    /// <summary>注入共享缓存、反馈服务和产品编辑器；由导航入口负责异步初始化。</summary>
    public CustomerProductWorkspaceViewModel(
        IReferenceDataCacheService referenceDataCache,
        IMessageService messageService,
        CustomerProductManagerViewModel editor)
    {
        _referenceDataCache = referenceDataCache;
        _messageService = messageService;
        Editor = editor;
        Title = "客户产品管理";
        Editor.PropertyChanged += OnEditorPropertyChanged;
    }

    /// <summary>承接当前客户产品编辑、保存和放弃变更确认。</summary>
    public CustomerProductManagerViewModel Editor { get; }
    /// <summary>提供本地选择器使用的稳定只读缓存；工作区不修改缓存 DTO。</summary>
    public ReadOnlyObservableCollection<CustomerDto> CustomerOptions => _referenceDataCache.Customers;
    /// <summary>根据编辑器当前对象生成页签标题。</summary>
    public string DocumentTitle => Editor.ActiveCustomer is null
        ? "客户产品管理"
        : $"客户产品管理 - {Editor.ActiveCustomer.CustomerCode}";
    /// <summary>将编辑器的真实脏状态提供给页签关闭检查。</summary>
    public bool HasUnsavedChanges => Editor.HasChanges;

    /// <summary>保存选择器确认的完整客户 DTO，仅在显式命令中切换编辑上下文。</summary>
    [ObservableProperty]
    public partial CustomerDto? SelectedCustomer { get; set; }

    /// <summary>确保会话缓存已加载；重复初始化共享同一任务，释放后不再启动加载。</summary>
    public Task InitializeAsync() => _disposed
        ? Task.CompletedTask
        : _initializationTask ??= LoadOptionsAsync();

    /// <summary>复用缓存服务的加载和会话隔离；页面释放后不再展示延迟反馈。</summary>
    private async Task LoadOptionsAsync()
    {
        var result = await _referenceDataCache.EnsureLoadedAsync();
        if (!_disposed && !result.Success)
        {
            _messageService.ShowWarning("基础资料加载失败", result.Message);
        }
    }

    /// <summary>将缓存 DTO 加载为编辑器模型；取消放弃变更时恢复原客户选择。</summary>
    [RelayCommand]
    private async Task CommitCustomerAsync()
    {
        var candidate = SelectedCustomer;
        if (_disposed || candidate is null)
        {
            return;
        }

        var model = new CustomerLineModel();
        model.Load(candidate);
        var accepted = await Editor.SelectCustomerAsync(model);
        if (_disposed)
        {
            return;
        }

        SynchronizeSelection(accepted ? model : Editor.ActiveCustomer);
    }

    /// <summary>显式清空选择前检查未保存变更，拒绝关闭时恢复当前客户。</summary>
    [RelayCommand]
    private async Task ClearCustomerAsync()
    {
        if (_disposed) return;
        if (!await Editor.CloseForNavigationAsync())
        {
            SynchronizeSelection(Editor.ActiveCustomer);
            return;
        }

        SynchronizeSelection(null);
    }

    /// <summary>委托编辑器决定页签是否允许关闭，并保留取消后的编辑状态。</summary>
    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Editor.CloseForNavigationAsync();
    }

    /// <summary>幂等解除编辑器订阅；共享会话缓存仍由缓存服务管理。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Editor.PropertyChanged -= OnEditorPropertyChanged;
    }

    /// <summary>同步标题、脏状态和编辑器主动关闭后的选择显示。</summary>
    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName is nameof(CustomerProductManagerViewModel.HasChanges)
                or nameof(CustomerProductManagerViewModel.ActiveCustomer)
                or nameof(CustomerProductManagerViewModel.IsOpen))
        {
            OnPropertyChanged(nameof(DocumentTitle));
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }

        if (e.PropertyName == nameof(CustomerProductManagerViewModel.IsOpen)
            && !Editor.IsOpen)
        {
            SynchronizeSelection(null);
        }
    }

    /// <summary>优先按稳定身份恢复缓存 DTO，缓存中已无此对象时保留编辑器快照。</summary>
    private void SynchronizeSelection(CustomerLineModel? customer)
    {
        SelectedCustomer = customer is null
            ? null
            : CustomerOptions.FirstOrDefault(item => item.Uid == customer.Uid) ?? customer.ToDto();
    }
}
