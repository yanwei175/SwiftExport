using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;

namespace SwiftExportClient.Features.DemandPlanning.GrossDemand.ViewModels;

public sealed partial class GrossDemandManualEditorViewModel : ViewModelBaseEx, IDisposable
{
    private readonly IGrossDemandWorkspaceApi _api;
    private readonly IProductApi _productApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private long _searchVersion;
    private bool _disposed;
    private CreateManualGrossDemandRequest? _pendingCreate;
    private UpdateManualGrossDemandRequest? _pendingUpdate;
    private bool _pendingUnknown;
    private bool _needsCurrentDetail;
    private Guid _confirmedUid;
    private bool _currentDetailEditable = true;
    private readonly ICurrentUserService _currentUser;

    public GrossDemandManualEditorViewModel(
        IGrossDemandWorkspaceApi api,
        IProductApi productApi,
        IApiCallRunner apiCallRunner,
        IMessageService messageService,
        ICurrentUserService currentUser)
    {
        _api = api;
        _productApi = productApi;
        _apiCallRunner = apiCallRunner;
        _messageService = messageService;
        _currentUser = currentUser;
        Title = "手工成品需求";
        Edit.PropertyChanged += OnEditPropertyChanged;
        PropertyChanged += OnOwnPropertyChanged;
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(SearchProductsCommand);
    }

    public GrossDemandManualEditModel Edit { get; } = new();
    public ObservableCollection<ProductLookupDto> ProductCandidates { get; } = [];
    public bool HasUnsavedChanges => IsOpen && Edit.IsDirty;
    public bool IsNew => Edit.IsNew;
    /// <summary>未知结果期间保留原请求并冻结真实表单；保存按钮仍可重试。</summary>
    public bool HasPendingSave => _pendingUnknown;
    /// <summary>请求发出至结果确认、或确认后当前详情未读到期间，禁止退出或切换编辑器。</summary>
    public bool IsEditorLocked => IsBusy || _pendingUnknown || _needsCurrentDetail;
    /// <summary>确认成功但当前详情读取失败时，原保存入口仅重试详情查询。</summary>
    public string SaveButtonText => _needsCurrentDetail ? "刷新详情" : "保存";
    /// <summary>表单输入是否可以修改，未决命令期间始终为否。</summary>
    public bool CanEditFields => IsOpen && CanWrite && !IsEditorLocked && _currentDetailEditable
        && _currentUser.HasPermission(PermissionKeys.DemandPlanningSourceManualManage);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    public partial ProductLookupDto? SelectedProduct { get; set; }

    [ObservableProperty]
    public partial bool IsLookupBusy { get; set; }

    public event EventHandler? Saved;

    public void OpenCreate()
    {
        if (IsEditorLocked) return;
        ClearLookup();
        Edit.ResetForCreate();
        _currentDetailEditable = true;
        IsOpen = true;
        Title = "新建手工毛需求";
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(IsNew));
    }

    public void OpenEdit(GrossDemandDetailDto detail)
    {
        if (IsEditorLocked) return;
        ClearLookup();
        Edit.Load(detail);
        _currentDetailEditable = detail.LifecycleStatus == GrossDemandLifecycleStatusEnum.Active;
        IsOpen = true;
        Title = $"编辑手工毛需求 {detail.SourceNo}";
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(IsNew));
    }

    public void Close(bool discardChanges)
    {
        if (IsEditorLocked) return;
        if (discardChanges)
        {
            Edit.AcceptChanges();
        }

        ClearLookup();
        IsOpen = false;
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchProductsAsync(string? keyword)
    {
        var version = Interlocked.Increment(ref _searchVersion);
        ProductCandidates.Clear();
        SelectedProduct = null;
        if (string.IsNullOrWhiteSpace(keyword) || Edit.IsNonStandard)
        {
            IsLookupBusy = false;
            return;
        }

        IsLookupBusy = true;
        var result = await _apiCallRunner.RunAsync(
            () => _productApi.SearchLookupAsync(new ProductLookupRequest
            {
                Keyword = keyword.Trim(),
                Limit = 20,
            }),
            "搜索产品失败");
        if (version != Volatile.Read(ref _searchVersion))
        {
            return;
        }

        if (result.Success && result.Data is not null)
        {
            foreach (var item in result.Data)
            {
                ProductCandidates.Add(item);
            }
        }

        IsLookupBusy = false;
    }

    [RelayCommand]
    private void CommitProduct()
    {
        if (SelectedProduct is null)
        {
            return;
        }

        Edit.ApplyProduct(SelectedProduct);
        SaveCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ClearProduct()
    {
        SelectedProduct = null;
        Edit.ProductId = null;
        Edit.Sku = string.Empty;
        Edit.LinkSku = string.Empty;
        Edit.ImagePath = string.Empty;
        SaveCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (_needsCurrentDetail)
        {
            await RunBusyAsync(LoadCurrentDetailAsync, "正在刷新手工毛需求详情...");
            return;
        }

        if (!_pendingUnknown && !Edit.ValidateForSave(out var validation))
        {
            ValidationSummary = validation;
            _messageService.ShowWarning("手工毛需求未保存", validation);
            return;
        }

        await RunWriteAsync(async () =>
        {
            if (!_pendingUnknown)
            {
                if (Edit.IsNew)
                {
                    _pendingCreate = Edit.ToCreateRequest();
                    _pendingCreate.CommandId = Guid.NewGuid();
                    _pendingUpdate = null;
                }
                else
                {
                    _pendingUpdate = Edit.ToUpdateRequest();
                    _pendingUpdate.CommandId = Guid.NewGuid();
                    _pendingCreate = null;
                }
            }
            var result = _pendingCreate is not null
                ? await _apiCallRunner.RunAsync(
                    () => _api.CreateManualAsync(_pendingCreate),
                    "新建手工毛需求失败")
                : await _apiCallRunner.RunAsync(
                    () => _api.UpdateManualAsync(_pendingUpdate!.Uid, _pendingUpdate),
                    "更新手工毛需求失败");
            if (!result.Success || result.Data is null)
            {
                var code = result.Error?.Code;
                var retain = code is ServiceErrorCodes.OutcomeUnknown or ServiceErrorCodes.CommandBusy or ServiceErrorCodes.ServiceUnavailable
                    || (_pendingUnknown && code is ServiceErrorCodes.RateLimited or ServiceErrorCodes.Unauthenticated or ServiceErrorCodes.Forbidden);
                SetPendingUnknown(retain);
                if (!retain) { _pendingCreate = null; _pendingUpdate = null; }
                ValidationSummary = result.Message;
                _messageService.ShowError("手工毛需求保存失败", result.Message);
                return;
            }

            _pendingCreate = null;
            _pendingUpdate = null;
            SetPendingUnknown(false);
            // 命令回执是首次提交的快照，必须重新读取当前事实才能恢复编辑。
            _confirmedUid = result.Data.Uid;
            SetNeedsCurrentDetail(true);
            _messageService.ShowSuccess("手工毛需求已保存", result.Data.SourceNo);
            Saved?.Invoke(this, EventArgs.Empty);
            await LoadCurrentDetailAsync();
        }, "正在保存手工毛需求...");
    }

    /// <summary>从服务端读取已确认命令的最新详情；读取失败时保持表单冻结并允许手动重试读取。</summary>
    private async Task LoadCurrentDetailAsync()
    {
        var result = await _apiCallRunner.RunAsync(
            () => _api.GetDetailAsync(_confirmedUid),
            "刷新手工毛需求详情失败");
        if (!result.Success || result.Data is null || result.Data.Uid != _confirmedUid)
        {
            ValidationSummary = "保存已确认，但当前详情读取失败。请点击“刷新详情”后继续编辑。";
            _messageService.ShowWarning("请刷新当前详情", ValidationSummary);
            return;
        }

        Edit.Load(result.Data);
        _currentDetailEditable = result.Data.LifecycleStatus == GrossDemandLifecycleStatusEnum.Active;
        Title = $"编辑手工毛需求 {result.Data.SourceNo}";
        ValidationSummary = null;
        _confirmedUid = Guid.Empty;
        SetNeedsCurrentDetail(false);
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(IsNew));
    }

    private bool CanSave() => IsOpen && CanWrite && IsNotBusy
        && _currentUser.HasPermission(PermissionKeys.DemandPlanningSourceManualManage)
        && (_needsCurrentDetail || _pendingUnknown || (_currentDetailEditable && Edit.IsDirty));

    /// <summary>Busy 和只读状态改变时同步真实表单输入权限。</summary>
    private void OnOwnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IsBusy) or nameof(IsReadOnly) or nameof(IsOpen))
        {
            OnPropertyChanged(nameof(CanEditFields));
            OnPropertyChanged(nameof(IsEditorLocked));
        }
    }

    /// <summary>通知表单冻结、重试及父页面关闭门禁随未决状态同步变化。</summary>
    private void SetPendingUnknown(bool value)
    {
        _pendingUnknown = value;
        OnPropertyChanged(nameof(HasPendingSave));
        OnPropertyChanged(nameof(CanEditFields));
        OnPropertyChanged(nameof(IsEditorLocked));
        SaveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>同步确认成功后只允许刷新当前详情的状态与保存按钮文案。</summary>
    private void SetNeedsCurrentDetail(bool value)
    {
        _needsCurrentDetail = value;
        OnPropertyChanged(nameof(IsEditorLocked));
        OnPropertyChanged(nameof(CanEditFields));
        OnPropertyChanged(nameof(SaveButtonText));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void OnEditPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void ClearLookup()
    {
        Interlocked.Increment(ref _searchVersion);
        ProductCandidates.Clear();
        SelectedProduct = null;
        IsLookupBusy = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        PropertyChanged -= OnOwnPropertyChanged;
        Edit.PropertyChanged -= OnEditPropertyChanged;
        ClearLookup();
    }
}
