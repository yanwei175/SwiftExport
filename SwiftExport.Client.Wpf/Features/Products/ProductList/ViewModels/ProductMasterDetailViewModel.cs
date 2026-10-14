using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Authentication;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.Components.Models;
using SwiftExportClient.Features.SystemSettings.Dictionaries.Models;
using SwiftExportClient.Features.SystemSettings.Dictionaries.ViewModels;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SwiftExportClient.Features.Products.ProductList.ViewModels;

/// <summary>主型号详情文档模型，负责头部、中性产品与模板标签的原子保存。</summary>
public sealed partial class ProductMasterDetailViewModel : ViewModelBaseEx, IDocumentTabViewModel, IDocumentTabInitializable, IDisposable
{
    /// <summary>获取主型号详情文档路由键。</summary>
    public const string RouteKey = "product-master-detail";
    private readonly IProductMasterApi _api;
    private readonly IApiCallRunner _runner;
    private readonly IReferenceDataCacheService? _cache;
    private readonly IMessageService _messages;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly ICurrentUserService? _currentUser;
    private CancellationTokenSource? _loadCts;
    private long _loadSequence;
    private bool _disposed;

    /// <summary>初始化详情页依赖和状态订阅。</summary>
    public ProductMasterDetailViewModel(
        IProductMasterApi api,
        IApiCallRunner runner,
        IReferenceDataCacheService cache,
        IMessageService messages,
        IDialogService dialogs,
        INavigationService navigation,
        ICurrentUserService? currentUser = null)
    {
        _api = api;
        _runner = runner;
        _cache = cache;
        _messages = messages;
        _dialogs = dialogs;
        _navigation = navigation;
        _currentUser = currentUser;
        Title = "父级详情";
        Header.PropertyChanged += OnHeaderPropertyChanged;
        Neutrals.ListStateChanged += OnNeutralListStateChanged;
        Tags.StateChanged += OnTagStateChanged;
        PropertyChanged += OnDetailPropertyChanged;
        if (_cache is not null) _cache.ProductAttributesChanged += OnProductAttributesChanged;
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(AddNeutralCommand);
        RegisterBusyAwareCommand(DeleteNeutralCommand);
        RegisterBusyAwareCommand(CommitCategoryCommand);
        RegisterBusyAwareCommand(ClearCategoryCommand);
        RegisterBusyAwareCommand(CommitProfileCommand);
        RegisterBusyAwareCommand(ClearProfileCommand);
        RegisterBusyAwareCommand(CommitSeriesCommand);
        RegisterBusyAwareCommand(ClearSeriesCommand);
        RegisterBusyAwareCommand(ManageSeriesCommand);
        RegisterBusyAwareCommand(CommitSupplierCommand);
        RegisterBusyAwareCommand(ClearSupplierCommand);
        if (_cache?.Suppliers is INotifyCollectionChanged suppliers) suppliers.CollectionChanged += OnSupplierCacheChanged;
        if (_currentUser is not null) _currentUser.CurrentUserChanged += OnCurrentUserChanged;
        ApplyEditPermission();
    }

    /// <summary>获取主型号头部编辑模型。</summary>
    public ProductMasterEditModel Header { get; } = new();
    /// <summary>获取中性产品明细集合。</summary>
    public ProductNeutralListModel Neutrals { get; } = new();
    /// <summary>获取本详情独占的模板标签工作区。</summary>
    public ProductAttributeSelectionModel Tags { get; } = new();
    /// <summary>获取基础资料缓存。</summary>
    public IReferenceDataCacheService? ReferenceDataCache => _cache;
    /// <summary>供应商选择使用当前会话的完整缓存，不再发起远程搜索。</summary>
    public IReadOnlyList<SupplierDto> SupplierOptions => _cache?.Suppliers ?? (IReadOnlyList<SupplierDto>)[];
    /// <summary>缓存缺失时保留当前供应商名称作为提示，不自动清除既有身份。</summary>
    public string SupplierPlaceholder => Header.DefaultSupplierId == Guid.Empty ? "选择供应商" : Header.DefaultSupplierName;
    /// <summary>获取文档标题。</summary>
    public string DocumentTitle => string.IsNullOrWhiteSpace(Header.MasterSku) ? "新建主型号" : $"主型号 · {Header.MasterSku}";
    /// <summary>获取头部、明细或标签是否存在未保存变更。</summary>
    public bool HasUnsavedChanges => Header.IsDirty || Neutrals.HasChanges || Tags.IsDirty;
    /// <summary>获取当前是否为新建文档。</summary>
    public bool IsCreateMode => Header.Uid == Guid.Empty;

    /// <summary>预览当前未删除中性产品中的默认主图，随编辑实时变化，不从服务端重新查询。</summary>
    public string? MainImagePath => Neutrals.Items.FirstOrDefault(line => line.IsMainImage)?.ImagePath;

    /// <summary>获取或设置当前中性产品行。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteNeutralCommand))]
    public partial ProductNeutralLineModel? SelectedNeutral { get; set; }
    /// <summary>获取或设置头部分类选择。</summary>
    [ObservableProperty] public partial ProductCategoryCacheNode? SelectedCategory { get; set; }
    /// <summary>获取或设置尚未确认的模板候选，不将搜索文本写入持久化字段。</summary>
    [ObservableProperty] public partial SysDictDataDto? SelectedProfile { get; set; }
    /// <summary>当前会话启用模板字典候选，中文名和英文代码由控件本地搜索。</summary>
    public IReadOnlyList<SysDictDataDto> ProfileCandidates => _cache?.GetDictionaryValues(DictCodes.ProductAttributeProfile) ?? [];
    /// <summary>系列候选来自当前会话启用字典；搜索文本不作为业务身份。</summary>
    public IReadOnlyList<SysDictDataDto> SeriesCandidates => _cache?.GetDictionaryValues(DictCodes.ProductSeries) ?? [];
    /// <summary>获取或设置尚未确认的系列候选。</summary>
    [ObservableProperty] public partial SysDictDataDto? SelectedSeries { get; set; }
    /// <summary>获取或设置头部默认供应商选择。</summary>
    [ObservableProperty] public partial SupplierDto? SelectedSupplier { get; set; }
    /// <summary>获取或设置中性产品颜色编辑器选择。</summary>
    [ObservableProperty] public partial ProductColorDto? EditColor { get; set; }
    /// <summary>获取详情加载与保存状态文本。</summary>
    [ObservableProperty] public partial string StatusText { get; set; } = "正在准备主型号详情。";
    /// <summary>获取或设置中性产品格子选择模式；格子模式保留单元格编辑，但禁止整行删除。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteNeutralCommand))]
    public partial bool IsCellSelectionMode { get; set; } = false;
    /// <summary>模板确认期间冻结标签与保存，避免旧模板草稿同时被修改。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditTags))]
    [NotifyPropertyChangedFor(nameof(CanEditHeader))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ManageSeriesCommand))]
    public partial bool IsProfileChangePending { get; private set; }
    /// <summary>保存与模板确认期间冻结头部编辑，避免请求或确认范围变化后再接纳旧快照。</summary>
    public bool CanEditHeader => CanWrite && IsNotBusy && !IsProfileChangePending;
    /// <summary>获取标签字段区是否可编辑，沿用详情的主型号编辑权限。</summary>
    public bool CanEditTags => CanWrite && IsNotBusy && !IsProfileChangePending && Tags.CanEdit;

    /// <summary>根据文档打开参数初始化新建状态或加载现有聚合。</summary>
    public async Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken)
    {
        var cacheResult = await _cache!.EnsureLoadedAsync();
        if (_disposed || cancellationToken.IsCancellationRequested) return;
        if (!cacheResult.Success) _messages.ShowError("加载基础资料失败", cacheResult.Message);
        RefreshTagCandidates();
        if (parameter is not ProductMasterDocumentOpenRequest request || request.ProductMasterId == Guid.Empty)
        {
            InitializeCreate();
            return;
        }
        await LoadAsync(request.ProductMasterId, cancellationToken);
    }

    /// <summary>关闭详情前检查真实变更集，写入进行中时拒绝关闭。</summary>
    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        if (IsBusy || IsProfileChangePending) return false;
        return !HasUnsavedChanges || await _dialogs.ConfirmAsync("放弃未保存变更", "主型号详情有未保存修改，确定关闭吗？", "关闭");
    }

    partial void OnSelectedNeutralChanged(ProductNeutralLineModel? value) => Neutrals.SelectedItem = value;

    [RelayCommand(CanExecute = nameof(CanAddNeutral))]
    private void AddNeutral()
    {
        if (!CanAddNeutral()) return;
        SelectedNeutral = Neutrals.AddNewNeutral(Header.Uid);
        NotifyDocumentState();
    }

    /// <summary>整行模式下删除中性产品；已保存行先确认，完成后选择剩余最后一行。</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteNeutral))]
    private async Task DeleteNeutralAsync()
    {
        if (!CanDeleteNeutral()) return;
        var lines = Neutrals.CheckedItems.Count > 0
            ? Neutrals.CheckedItems.ToArray()
            : SelectedNeutral is null ? [] : [SelectedNeutral];
        if (lines.Length == 0) return;
        if (lines.Any(line => !line.IsNew)
            && !await _dialogs.ConfirmAsync("删除中性产品", $"确定删除选中的 {lines.Length} 条中性产品吗？保存后生效。", "删除")) return;
        foreach (var line in lines) Neutrals.Remove(line);
        SelectedNeutral = Neutrals.Items.LastOrDefault();
        NotifyDocumentState();
    }

    /// <summary>校验后原子保存主型号、中性产品及标签，失败保留详情草稿。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (_disposed || IsBusy || IsProfileChangePending) return;
        await RunWriteAsync(async () =>
        {
            var headerError = Header.ValidateRow();
            var lineError = await Neutrals.ValidateAllAsync();
            var error = headerError ?? lineError ?? Tags.ValidateSelection(Header.ProductAttributeProfileId ?? Guid.Empty);
            if (!string.IsNullOrWhiteSpace(error))
            {
                ValidationSummary = error;
                _messages.ShowWarning("主型号未保存", error);
                return;
            }
            // 校验可能等待异步结果，发送前再次检查文档存活与当前编辑权限。
            if (_disposed) return;
            if (!CanWrite)
            {
                _messages.ShowWarning("主型号未保存", "编辑权限已收回，本地草稿已保留。");
                return;
            }
            var request = new SaveProductMasterDetailRequest
            {
                ContractVersion = 3,
                ProductMasterId = Header.Uid,
                ExpectedVersion = Header.Uid == Guid.Empty ? 0 : Header.Version,
                Master = Header.ToWriteDto(),
                SelectedOptionIds = Tags.GetSelectedOptionIds()
            };
            Neutrals.ApplyChanges(request);
            var result = await _runner.RunAsync(() => _api.SaveDetailAsync(request), "保存主型号详情失败");
            if (_disposed) return;
            if (!result.Success || result.Data?.Detail is null)
            {
                ValidationSummary = result.Message;
                if (result.Error?.ErrorCodeIs(ServiceErrorCodes.VersionConflict) == true)
                    _messages.ShowWarning("版本冲突", "数据已被其他用户修改，本地编辑已保留。请决定是否刷新。");
                else
                    _messages.ShowError("保存主型号详情失败", result.Message);
                return;
            }
            var wasCreate = Header.Uid == Guid.Empty;
            AcceptDetail(result.Data.Detail);
            if (wasCreate) _navigation.UpdateDocumentKey(this, $"product-master:{Header.Uid:N}");
            WeakReferenceMessenger.Default.Send(new ProductMasterDetailSavedMessage(Header.Uid));
            _messages.ShowSuccess("主型号已保存", Header.MasterSku);
        }, "正在保存主型号详情...");
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_disposed || IsBusy || IsProfileChangePending) return;
        if (Header.Uid == Guid.Empty) return;
        if (HasUnsavedChanges && !await _dialogs.ConfirmAsync("放弃未保存变更", "刷新将放弃当前详情修改，是否继续？", "刷新")) return;
        await LoadAsync(Header.Uid, CancellationToken.None);
    }

    /// <summary>撤销头部、明细与标签，并同步 Lookup 选择，避免失焦恢复已撤销字段。</summary>
    [RelayCommand]
    private void RejectDetailChanges()
    {
        if (_disposed || IsBusy || IsProfileChangePending || Tags.IsPopupOpen) return;
        Header.RejectChanges();
        SyncSelectedSupplier();
        SyncSelectedSeries();
        SelectedCategory = _cache?.ProductCategories.FirstOrDefault(item => item.Uid == Header.CategoryId);
        SyncSelectedProfile();
        Neutrals.RejectChanges();
        Tags.RejectChanges();
        RefreshTagCandidates();
        SelectedNeutral = Neutrals.Items.FirstOrDefault();
        NotifyDocumentState();
    }

    [RelayCommand]
    private Task CloseDocumentAsync() => _navigation.CloseAsync(
        _navigation.OpenTabs.FirstOrDefault(tab => ReferenceEquals(tab.ContentViewModel, this)));

    /// <summary>只刷新共享分类缓存，不重新加载详情或接纳未保存编辑；关闭后不再反馈结果。</summary>
    [RelayCommand]
    private async Task RefreshProductCategoriesAsync()
    {
        if (_disposed || IsBusy || IsProfileChangePending) return;
        // 缓存刷新是会话级共享操作，当前接口不接受取消；页面只消费存活期间的结果。
        var result = await _cache!.RefreshProductCategoriesAsync();
        if (_disposed) return;
        if (!result.Success)
        {
            _messages.ShowError("刷新产品分类失败", result.Message);
            return;
        }
        SelectedCategory = _cache?.ProductCategories.FirstOrDefault(item => item.Uid == Header.CategoryId);
        RefreshTagCandidates();
    }

    /// <summary>分类只维护分类资料，不改变属性模板或标签选择。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeCategory))]
    private Task CommitCategoryAsync()
    {
        if (CanChangeCategory() && SelectedCategory is { } category)
        {
            Header.CategoryId = category.Uid;
            Header.CategoryName = category.CategoryName;
        }
        return Task.CompletedTask;
    }

    /// <summary>显式清空分类，模板和所有标签草稿保持不变。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeCategory))]
    private Task ClearCategoryAsync()
    {
        if (CanChangeCategory())
        {
            Header.CategoryId = Guid.Empty;
            Header.CategoryName = string.Empty;
            SelectedCategory = null;
        }
        return Task.CompletedTask;
    }

    /// <summary>模板候选确认后保留共同适用标签，仅确认清除不再适用的选择。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeProfile))]
    private Task CommitProfileAsync() => SelectedProfile is { } profile
        ? ChangeProfileAsync(profile.Uid, profile.DataNameZh ?? profile.DataNameEn ?? string.Empty) : Task.CompletedTask;

    /// <summary>清空模板遵守标签确认协议；取消恢复原模板和全部选择。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeProfile))]
    private Task ClearProfileAsync() => ChangeProfileAsync(null, string.Empty);

    /// <summary>共用模板确认协议，确认期间缓存变化须重验，不能扩大已同意清除的范围。</summary>
    private async Task ChangeProfileAsync(Guid? profileId, string profileName)
    {
        if (!CanChangeProfile()) return;
        var previousId = Header.ProductAttributeProfileId;
        if (previousId == profileId) return;
        IsProfileChangePending = true;
        try
        {
            var resolution = ResolveTagDefinitions(profileId ?? Guid.Empty);
            if (!resolution.Success)
            {
                SyncSelectedProfile();
                _messages.ShowWarning("无法更换属性模板", $"{resolution.Message} 当前模板和标签已保留，请刷新后重试。");
                return;
            }
            var inapplicableIds = Tags.GetInapplicableOptionIds(resolution);
            var confirmed = inapplicableIds.Count == 0 || await _dialogs.ConfirmAsync("更换属性模板",
                $"有 {inapplicableIds.Count} 个已选标签在目标模板不再适用。确认后仅清除这些标签，其余标签保留，整页保存后生效。是否继续？", "清除失效标签并更换");
            if (_disposed) return;
            if (!confirmed || !CanWrite)
            {
                SyncSelectedProfile();
                return;
            }
            var latest = ResolveTagDefinitions(profileId ?? Guid.Empty);
            if (!latest.Success || !inapplicableIds.SequenceEqual(Tags.GetInapplicableOptionIds(latest)))
            {
                SyncSelectedProfile();
                _messages.ShowWarning("模板标签已更新", "标签适用范围在确认期间发生变化，当前模板和选择已保留，请重新选择模板并核对。");
                return;
            }
            Header.ProductAttributeProfileId = profileId;
            Header.ProductAttributeProfileName = profileName;
            Tags.ChangeProfile(profileId ?? Guid.Empty, latest);
            SyncSelectedProfile();
        }
        finally
        {
            if (!_disposed)
            {
                IsProfileChangePending = false;
                NotifyDocumentState();
            }
        }
    }

    /// <summary>恢复头部模板的可见选择，缺失字典明细保留身份及已保存名称。</summary>
    private void SyncSelectedProfile()
    {
        SelectedProfile = Header.ProductAttributeProfileId is not { } id ? null :
            ProfileCandidates.FirstOrDefault(row => row.Uid == id) ?? new SysDictDataDto
            { Uid = id, DictCode = DictCodes.ProductAttributeProfile, DataNameZh = Header.ProductAttributeProfileName };
    }

    /// <summary>独立刷新标签定义，保留主型号头部、中性产品与已确认标签草稿。</summary>
    [RelayCommand]
    private async Task RefreshProductAttributesAsync()
    {
        if (_disposed || IsBusy || IsProfileChangePending) return;
        if (Tags.IsPopupOpen)
        {
            _messages.ShowWarning("标签尚未确认", "请先确定或关闭标签选择弹出层，再刷新标签。");
            return;
        }
        await RunBusyAsync(async () =>
        {
            var result = await _cache!.RefreshProductAttributesAsync();
            if (_disposed) return;
            if (!result.Success) _messages.ShowError("刷新标签失败", result.Message);
            RefreshTagCandidates();
        }, "正在刷新标签定义...");
    }

    private bool CanChangeProfile() => !_disposed && CanWrite && IsNotBusy && !IsProfileChangePending && !Tags.IsPopupOpen;

    private bool CanChangeCategory() => !_disposed && CanWrite && IsNotBusy && !IsProfileChangePending && !Tags.IsPopupOpen;

    /// <summary>仅在确认候选时同步供应商标识和名称，不将搜索文本写入头部。</summary>
    [RelayCommand(CanExecute = nameof(CanEditLookup))]
    private void CommitSupplier()
    {
        if (!CanEditLookup() || SelectedSupplier is null) return;
        Header.DefaultSupplierId = SelectedSupplier.Uid;
        Header.DefaultSupplierName = SelectedSupplier.SupplierName ?? string.Empty;
    }

    /// <summary>显式清空可选供应商，同时清除头部字段和控件选择。</summary>
    [RelayCommand(CanExecute = nameof(CanEditLookup))]
    private void ClearSupplier()
    {
        if (!CanEditLookup()) return;
        Header.DefaultSupplierId = Guid.Empty;
        Header.DefaultSupplierName = string.Empty;
        SelectedSupplier = null;
    }

    /// <summary>根据已加载或撤销后的头部重建显示选择，不触发提交或重新接纳编辑基线。</summary>
    private void SyncSelectedSupplier()
    {
        SelectedSupplier = Header.DefaultSupplierId == Guid.Empty
            ? null
            : SupplierOptions.FirstOrDefault(item => item.Uid == Header.DefaultSupplierId)
              ?? new SupplierDto { Uid = Header.DefaultSupplierId, SupplierName = Header.DefaultSupplierName };
    }

    /// <summary>确认有效系列候选后才修改系列身份，允许字典显示名更新而不改引用。</summary>
    [RelayCommand(CanExecute = nameof(CanEditLookup))]
    private void CommitSeries()
    {
        if (!CanEditLookup() || SelectedSeries is null
            || !SeriesCandidates.Any(item => item.Uid == SelectedSeries.Uid)) return;
        Header.ProductSeriesId = SelectedSeries.Uid;
        Header.ProductSeriesName = SelectedSeries.DataNameZh ?? SelectedSeries.DataNameEn ?? string.Empty;
    }

    /// <summary>明确清空可选系列，只修改当前头部，不接纳其他字段草稿。</summary>
    [RelayCommand(CanExecute = nameof(CanEditLookup))]
    private void ClearSeries()
    {
        if (!CanEditLookup()) return;
        Header.ProductSeriesId = null;
        Header.ProductSeriesName = string.Empty;
        SelectedSeries = null;
    }

    private bool CanEditLookup() => !_disposed && CanEditHeader;

    /// <summary>打开系列字典明细，不提交或替换当前主型号的系列选择与其他草稿。</summary>
    [RelayCommand(CanExecute = nameof(CanManageSeries))]
    private async Task ManageSeriesAsync()
    {
        if (!CanManageSeries()) return;
        var request = new DictionaryDataDocumentOpenRequest(DictCodes.ProductSeries, "产品系列",
            "系列明细与字典管理共用；已被主型号使用的系列不能删除、停用，改名保持引用。");
        await _navigation.OpenDocumentAsync(DictionaryDataManagerViewModel.RouteKey, request.DocumentKey, request);
    }

    private bool CanManageSeries() => !_disposed && IsNotBusy && !IsProfileChangePending && !Tags.IsPopupOpen
        && (_currentUser?.HasPermission("System.Dictionary.View") ?? true);

    /// <summary>按身份恢复系列候选；缺失或停用项保留原身份与提示，不自动清空。</summary>
    private void SyncSelectedSeries()
    {
        SelectedSeries = Header.ProductSeriesId is not { } id ? null
            : SeriesCandidates.FirstOrDefault(item => item.Uid == id)
              ?? new SysDictDataDto { Uid = id, DictCode = DictCodes.ProductSeries,
                  DataNameZh = Header.ProductSeriesName, IsEnabled = false };
        if (SelectedSeries is not null) Header.ProductSeriesName = SelectedSeries.DataNameZh ?? SelectedSeries.DataNameEn ?? string.Empty;
    }

    /// <summary>供应商快照替换只恢复显示对象，不改供应商身份或接纳业务草稿。</summary>
    private void OnSupplierCacheChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (!_disposed) SyncSelectedSupplier();
    }

    [RelayCommand]
    private void CommitColor(ProductNeutralLineModel? line)
    {
        if (line is null || EditColor is null) return;
        line.ColorId = EditColor.Uid;
        line.ColorName = EditColor.ColorNameZh ?? EditColor.ColorNameEn ?? string.Empty;
        EditColor = null;
    }

    [RelayCommand]
    private void SetMainImage(ProductNeutralLineModel? line)
    {
        if (line is null) return;
        Neutrals.SetMainImage(line);
    }

    private void InitializeCreate()
    {
        Header.Load(new ProductMasterDetailDto { Master = new ProductMasterWriteDto { DefaultPcsPerPackage = 1 } });
        Header.MarkAsAdded();
        SyncSelectedSeries();
        Neutrals.Clear();
        Tags.Load(Header.ProductAttributeProfileId ?? Guid.Empty, [], []);
        RefreshTagCandidates();
        AddNeutral();
        StatusText = "请维护主型号头部和中性产品。";
        NotifyDocumentState();
    }

    private async Task LoadAsync(Guid productMasterId, CancellationToken externalToken)
    {
        var sequence = Interlocked.Increment(ref _loadSequence);
        var currentCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        var previousCts = Interlocked.Exchange(ref _loadCts, currentCts);
        previousCts?.Cancel();
        previousCts?.Dispose();
        await RunBusyAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.GetDetailAsync(productMasterId, currentCts.Token), currentCts.Token, "加载主型号详情失败");
            if (_disposed || currentCts.IsCancellationRequested || sequence != Volatile.Read(ref _loadSequence)) return;
            if (!result.Success || result.Data is null)
            {
                StatusText = result.Message;
                if (!string.IsNullOrWhiteSpace(result.Message)) _messages.ShowError("加载主型号详情失败", result.Message);
                return;
            }
            AcceptDetail(result.Data);
        }, "正在加载主型号详情...");
    }

    private void AcceptDetail(ProductMasterDetailDto detail)
    {
        Header.Load(detail);
        Neutrals.LoadFromDtos(detail.Neutrals ?? []);
        SelectedNeutral = Neutrals.Items.FirstOrDefault();
        SelectedCategory = _cache?.ProductCategories.FirstOrDefault(item => item.Uid == Header.CategoryId);
        SyncSelectedSupplier();
        SyncSelectedSeries();
        SyncSelectedProfile();
        Tags.Load(Header.ProductAttributeProfileId ?? Guid.Empty, detail.SelectedOptionIds, detail.AttributeSelections);
        RefreshTagCandidates();
        StatusText = $"已加载 {Neutrals.Items.Count} 条中性产品。";
        AcceptChanges();
        NotifyDocumentState();
    }

    private void OnHeaderPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Header.DefaultSupplierId) or nameof(Header.DefaultSupplierName))
            OnPropertyChanged(nameof(SupplierPlaceholder));
        NotifyDocumentState();
    }
    private void OnTagStateChanged(object? sender, EventArgs e) => NotifyDocumentState();
    private void OnDetailPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IsBusy) or nameof(CanWrite))
        { OnPropertyChanged(nameof(CanEditTags)); OnPropertyChanged(nameof(CanEditHeader)); }
    }

    /// <summary>身份快照变化时收回详情写能力，不清空已有草稿；后端授权仍是最终依据。</summary>
    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        ApplyEditPermission();
        NotifyDocumentState();
    }

    /// <summary>详情聚合及标签沿用 Master.Edit；离线测试省略身份依赖时保留既有行为。</summary>
    private void ApplyEditPermission() => IsReadOnly = _currentUser is not null && !_currentUser.HasPermission(PermissionKeys.ProductsMasterEdit);

    /// <summary>共享定义缓存更新只重匹配候选，不接纳详情的编辑基线。</summary>
    private void OnProductAttributesChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            OnPropertyChanged(nameof(ProfileCandidates));
            OnPropertyChanged(nameof(SeriesCandidates));
            SyncSelectedSeries();
            SyncSelectedProfile();
            RefreshTagCandidates();
        }
    }

    private void RefreshTagCandidates()
    {
        if (_cache is null) return;
        Tags.RefreshDefinitions(ResolveTagDefinitions(Header.ProductAttributeProfileId ?? Guid.Empty));
    }

    /// <summary>没有模板为正常空范围；非空模板必须使用字典与标签的完整解析结果。</summary>
    private ProductAttributeResolution ResolveTagDefinitions(Guid profileId) => profileId == Guid.Empty
        ? new(true, "请先选择产品属性模板。", [], [])
        : _cache?.ResolveProductAttributes(profileId) ?? new(false, "标签定义尚未加载，请刷新标签。", [], []);
    /// <summary>明细属性、增删或回滚变化时同步主图预览及文档状态。</summary>
    private void OnNeutralListStateChanged(object? sender, ListStateChangedEventArgs<ProductNeutralLineModel> e)
    {
        OnPropertyChanged(nameof(MainImagePath));
        NotifyDocumentState();
    }

    private void NotifyDocumentState()
    {
        IsDirty = HasUnsavedChanges;
        OnPropertyChanged(nameof(DocumentTitle));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(IsCreateMode));
        SaveCommand.NotifyCanExecuteChanged();
        DeleteNeutralCommand.NotifyCanExecuteChanged();
        CommitCategoryCommand.NotifyCanExecuteChanged();
        ClearCategoryCommand.NotifyCanExecuteChanged();
        CommitProfileCommand.NotifyCanExecuteChanged();
        ClearProfileCommand.NotifyCanExecuteChanged();
        ManageSeriesCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanEditTags));
    }

    private bool CanSave() => !_disposed && IsNotBusy && CanWrite && !IsProfileChangePending && HasUnsavedChanges;
    private bool CanAddNeutral() => !_disposed && IsNotBusy && CanWrite;
    /// <summary>格子模式不提供整行删除，勾选项也不能绕过模式限制。</summary>
    private bool CanDeleteNeutral() => !IsCellSelectionMode && IsNotBusy && CanWrite
        && (Neutrals.CheckedCount > 0 || SelectedNeutral is not null);

    /// <summary>取消详情加载并解除模型事件订阅。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Header.PropertyChanged -= OnHeaderPropertyChanged;
        Neutrals.ListStateChanged -= OnNeutralListStateChanged;
        Tags.StateChanged -= OnTagStateChanged;
        PropertyChanged -= OnDetailPropertyChanged;
        if (_cache is not null) _cache.ProductAttributesChanged -= OnProductAttributesChanged;
        if (_cache?.Suppliers is INotifyCollectionChanged suppliers) suppliers.CollectionChanged -= OnSupplierCacheChanged;
        if (_currentUser is not null) _currentUser.CurrentUserChanged -= OnCurrentUserChanged;
        Tags.Dispose();
        var cts = Interlocked.Exchange(ref _loadCts, null);
        cts?.Cancel();
        cts?.Dispose();
    }
}

internal static class ProductMasterServiceResultExtensions
{
    public static bool ErrorCodeIs(this ServiceErrorDto error, string errorCode) =>
        string.Equals(error.Code, errorCode, StringComparison.Ordinal);
}
