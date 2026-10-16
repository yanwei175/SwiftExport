using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Products.Colors.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.ComponentModel;
using System.Windows.Data;

namespace SwiftExportClient.Features.Products.Colors.ViewModels;

/// <summary>维护完整颜色列表、本地搜索及全局顺序；排序和资料编辑统一通过原批量保存提交。</summary>
public sealed partial class ProductColorsViewModel : ViewModelBaseEx, IAsyncInitializable, IDisposable
{
    private readonly IProductColorApi _colorApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private readonly IDialogService _dialogService;
    private readonly IReferenceDataCacheService _referenceDataCache;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private bool _isInitialized;
    private bool _disposed;
    private int _loadVersion;

    /// <summary>获取或设置本地搜索词；保留全部未保存编辑，但禁止在筛选结果中移动行。</summary>
    [ObservableProperty]
    public partial string Keyword { get; set; } = string.Empty;

    /// <summary>获取或设置当前行，作为删除操作的目标。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    public partial ProductColorLineModel? SelectedLine { get; set; }

    /// <summary>获取或设置完整列表及本地搜索匹配数量提示。</summary>
    [ObservableProperty]
    public partial string ResultSummary { get; set; } = "尚未加载产品颜色。";

    /// <summary>获取或设置当前是否存在需要用户处理的校验错误。</summary>
    [ObservableProperty]
    public partial bool HasValidationError { get; set; }

    /// <summary>获取或设置表格是否采用单元格选择模式。</summary>
    [ObservableProperty]
    public partial bool IsCellSelectionMode { get; set; }

    /// <summary>创建颜色维护页，复用列表脏跟踪及表格原生视图，不创建第二份业务集合。</summary>
    public ProductColorsViewModel(IProductColorApi colorApi, IApiCallRunner apiCallRunner,
        IMessageService messageService, IDialogService dialogService, IReferenceDataCacheService referenceDataCache)
    {
        _colorApi = colorApi;
        _apiCallRunner = apiCallRunner;
        _messageService = messageService;
        _dialogService = dialogService;
        _referenceDataCache = referenceDataCache;
        Title = "产品颜色管理";
        Lines = new BaseListModel<ProductColorLineModel, ProductColorDto>(_ => new ProductColorLineModel());
        ColorsView = CollectionViewSource.GetDefaultView(Lines.Items);
        Lines.ListStateChanged += OnListStateChanged;
        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(AddRowCommand);
        RegisterBusyAwareCommand(DeleteSelectedCommand);
        RegisterBusyAwareCommand(RejectAllChangesCommand);
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(MoreCommand);
        RegisterBusyAwareCommand(ReorderLineCommand);
    }

    /// <summary>获取页面维护职责说明。</summary>
    public string Description { get; } = "维护产品颜色资料及全局显示顺序。";

    /// <summary>获取拥有完整编辑状态、删除池和已接纳顺序基线的列表。</summary>
    public BaseListModel<ProductColorLineModel, ProductColorDto> Lines { get; }

    /// <summary>获取表格本地视图；无搜索时不设置 Filter，允许重排完整列表。</summary>
    public ICollectionView ColorsView { get; }

    /// <summary>获取当前是否允许拖拽重排；搜索、只读、忙碌和视图变换期间禁止重排。</summary>
    public bool CanReorder => CanMoveLines();

    /// <summary>获取是否存在待提交的资料或排序变更。</summary>
    public bool HasChanges => Lines.HasChanges;

    /// <summary>获取当前视图是否没有匹配的颜色。</summary>
    public bool IsEmpty => ColorsView.IsEmpty;

    /// <summary>首次进入时加载完整颜色快照；失败允许重新初始化。</summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized || _disposed) return;
        _isInitialized = true;
        if (!await LoadAllAsync(confirmChanges: false)) _isInitialized = false;
    }

    /// <summary>显式搜索仅刷新本地视图，不请求 API 或丢弃编辑。</summary>
    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private void Search() => ApplyLocalFilter();

    /// <summary>经未保存确认后重新加载完整列表。</summary>
    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private Task RefreshAsync() => LoadAllAsync(confirmChanges: true);

    /// <summary>新增颜色追加到全局末尾；清空本地搜索以便立即编辑新增行。</summary>
    [RelayCommand(CanExecute = nameof(CanWriteCommand))]
    private void AddRow()
    {
        if (!CanWriteCommand()) return;
        var maxSortNo = Lines.Items.Select(line => line.SortNo).DefaultIfEmpty(0).Max();
        if (maxSortNo == int.MaxValue)
        {
            _messageService.ShowWarning("无法新增颜色", "排序号已达上限，请先调整颜色顺序后重试。");
            return;
        }
        Keyword = string.Empty;
        SelectedLine = Lines.AddNew(new ProductColorDto
        {
            Uid = Guid.Empty, ColorCodeUnique = string.Empty, ColorCode = string.Empty,
            ColorNameEn = string.Empty, ColorNameZh = string.Empty, SortNo = maxSortNo + 1,
        });
        UpdateChangeState();
    }

    /// <summary>移除当前行并保留原删除池，保存前可撤销，不强制重编号其他颜色。</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteSelectedAsync()
    {
        if (!CanDeleteSelected()) return;
        var line = SelectedLine!;
        var code = string.IsNullOrWhiteSpace(line.ColorCodeUnique) ? line.ColorCode : line.ColorCodeUnique;
        if (!await _dialogService.ConfirmAsync("删除产品颜色", $"确定要删除颜色 {code} 吗？保存前可以撤销。", "删除")
            || !CanWriteCommand()) return;
        Lines.Remove(line);
        SelectedLine = ColorsView.Cast<ProductColorLineModel>().FirstOrDefault();
        UpdateChangeState();
    }

    /// <summary>恢复已接纳字段和集合顺序，清除校验提示，保留当前搜索条件。</summary>
    [RelayCommand(CanExecute = nameof(CanRejectChanges))]
    private void RejectAllChanges()
    {
        if (!CanRejectChanges()) return;
        Lines.RejectChanges();
        RejectChanges();
        SetValidationFeedback(null);
        ApplyLocalFilter();
        SelectedLine = ColorsView.Cast<ProductColorLineModel>().FirstOrDefault();
    }

    /// <summary>接纳当前业务行锚点，拖拽和键盘共用完整列表重排入口。</summary>
    [RelayCommand(CanExecute = nameof(CanReorderLine))]
    private void ReorderLine(RowReorderRequest? request)
    {
        if (CanReorderLine(request) && request!.TryResolve(Lines.Items, out var line, out var index)) MoveLineTo(line, index);
    }

    private bool CanReorderLine(RowReorderRequest? request) => CanReorder
        && request is not null && request.TryResolve<ProductColorLineModel>(Lines.Items, out _, out _);

    /// <summary>校验当前集合成员和移动后的最终索引；过期行、越界及原地移动均不接纳。</summary>
    public bool CanMoveLineTo(ProductColorLineModel line, int newIndex) => CanReorder
        && Lines.Items.IndexOf(line) is var oldIndex && oldIndex >= 0
        && newIndex >= 0 && newIndex < Lines.Items.Count && oldIndex != newIndex;

    /// <summary>将颜色移到最终索引并重编号；保留对象身份、当前选择及原有保存和撤销基线。</summary>
    public bool MoveLineTo(ProductColorLineModel line, int newIndex)
    {
        if (!CanMoveLineTo(line, newIndex)) return false;
        using (Lines.SuppressListEvents())
        {
            if (!Lines.Move(line, newIndex)) return false;
            for (var index = 0; index < Lines.Items.Count; index++) Lines.Items[index].SortNo = index + 1;
        }
        SelectedLine = line;
        UpdateChangeState();
        return true;
    }

    /// <summary>冻结变更集后批量提交；失败保留编辑，成功再接纳并刷新列表及共享颜色缓存。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave()) return;
        await RunWriteAsync(async () =>
        {
            var validationError = await Lines.ValidateAllAsync();
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                SetValidationFeedback(validationError);
                _messageService.ShowWarning("产品颜色未保存", validationError);
                return;
            }
            var changeSet = Lines.GetChangeSet();
            var request = new ProductColorBatchSaveRequest
            {
                CreateItems = changeSet.Added.Select(color => new CreateItemRequest<ProductColorDto> { Data = color }).ToList(),
                UpdateItems = changeSet.Modified.Select(color => new UpdateItemRequest<ProductColorDto>
                { Uid = color.Uid, Version = color.Version, Data = color }).ToList(),
                DeleteItems = changeSet.Deleted.Where(color => color.Uid != Guid.Empty).Select(color => new DeleteItemRequest
                { Uid = color.Uid, Version = color.Version, IsSoftDelete = true }).ToList(),
            };
            if (!request.HasChanges)
            {
                Lines.AcceptChanges();
                AcceptChanges();
                UpdateChangeState();
                return;
            }
            // 写请求不使用可替代查询的取消策略，不自动重放。
            var result = await _apiCallRunner.RunAsync(() => _colorApi.BatchSaveAsync(request), "保存产品颜色失败");
            if (_disposed) return;
            if (!result.Success)
            {
                SetValidationFeedback(result.Message);
                _messageService.ShowError("保存产品颜色失败", result.Message);
                return;
            }
            Lines.AcceptChanges();
            AcceptChanges();
            SetValidationFeedback(null);
            UpdateChangeState();
            _messageService.ShowSuccess("产品颜色已保存", $"已提交 {result.Data?.SuccessCount ?? 0} 条变更。");
            await LoadAllCoreAsync();
            if (_disposed) return;
            var cacheResult = await _referenceDataCache.RefreshProductColorsAsync();
            if (!_disposed && !cacheResult.Success)
                _messageService.ShowWarning("产品颜色已保存，但缓存刷新失败", cacheResult.Message);
        }, "正在保存产品颜色...");
    }

    /// <summary>刷新拥有最外层 Busy；保存后的重载直接调用 Core 方法。</summary>
    private async Task<bool> LoadAllAsync(bool confirmChanges)
    {
        if (!CanRunCommand()) return false;
        if (confirmChanges && !await EnsureCanDiscardChangesAsync()) return false;
        var loaded = false;
        await RunBusyAsync(async () => loaded = await LoadAllCoreAsync(), "正在加载产品颜色...");
        return loaded;
    }

    /// <summary>只接纳存活页面的最新完整快照，刷新失败不覆盖现有编辑。</summary>
    private async Task<bool> LoadAllCoreAsync()
    {
        var version = ++_loadVersion;
        var token = _lifetimeCancellation.Token;
        var result = await _apiCallRunner.RunAsync(() => _colorApi.GetAllAsync(), token, "加载产品颜色失败");
        if (_disposed || token.IsCancellationRequested || version != _loadVersion) return false;
        if (!result.Success)
        {
            SetValidationFeedback(result.Message);
            ResultSummary = "产品颜色加载失败。";
            _messageService.ShowError("加载产品颜色失败", result.Message);
            return false;
        }
        Lines.LoadFromDtos(result.Data ?? []);
        SetValidationFeedback(null);
        AcceptChanges();
        ApplyLocalFilter();
        SelectedLine = ColorsView.Cast<ProductColorLineModel>().FirstOrDefault();
        return true;
    }

    /// <summary>刷新会替换完整编辑快照，须确认放弃变更；本地搜索不经过此入口。</summary>
    private Task<bool> EnsureCanDiscardChangesAsync() => !Lines.HasChanges ? Task.FromResult(true)
        : _dialogService.ConfirmAsync("放弃未保存变更", "当前产品颜色列表有未保存的修改。继续操作会放弃这些修改。", "继续");

    /// <summary>保留原更多功能占位行为，不扩展本次排序范围。</summary>
    [RelayCommand]
    private void More(string? actionName) => _messageService.ShowInfo("产品颜色管理",
        $"{(string.IsNullOrWhiteSpace(actionName) ? "该功能" : actionName)}将在后续阶段接入。");

    /// <summary>只在本地视图筛选；空搜索必须移除 Filter，恢复完整列表重排。</summary>
    private void ApplyLocalFilter()
    {
        if (_disposed) return;
        var keyword = Keyword.Trim();
        ColorsView.Filter = keyword.Length == 0 ? null : item => item is ProductColorLineModel line
            && new[] { line.ColorCodeUnique, line.ColorCode, line.ColorNameEn, line.ColorNameZh }
                .Any(text => text.Contains(keyword, StringComparison.CurrentCultureIgnoreCase));
        UpdateChangeState();
    }

    private bool CanRunCommand() => !_disposed && IsNotBusy;
    private bool CanWriteCommand() => CanRunCommand() && CanWrite;
    private bool CanDeleteSelected() => CanWriteCommand() && SelectedLine is not null;
    private bool CanRejectChanges() => CanRunCommand() && Lines.HasChanges;
    private bool CanSave() => CanWriteCommand() && Lines.HasChanges;

    /// <summary>禁止在被排序、筛选或分组的视图移动行，即使外部直接调用命令也不能绕过。</summary>
    private bool CanMoveLines() => CanWriteCommand() && string.IsNullOrWhiteSpace(Keyword)
        && ColorsView.Filter is null && ColorsView.SortDescriptions.Count == 0 && ColorsView.GroupDescriptions.Count == 0
        && ColorsView is not ListCollectionView { CustomSort: not null };

    /// <summary>列表字段、增删和顺序变化统一刷新编辑提示及重排入口。</summary>
    private void OnListStateChanged(object? sender, ListStateChangedEventArgs<ProductColorLineModel> e) => UpdateChangeState();

    /// <summary>只统计当前视图，不在单元格编辑过程中刷新视图或替换业务集合。</summary>
    private void UpdateChangeState()
    {
        IsDirty = Lines.HasChanges;
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanReorder));
        ResultSummary = $"共 {Lines.Items.Count} 条，当前显示 {ColorsView.Cast<object>().Count()} 条。";
        SaveCommand.NotifyCanExecuteChanged();
        RejectAllChangesCommand.NotifyCanExecuteChanged();
        ReorderLineCommand.NotifyCanExecuteChanged();
    }

    partial void OnKeywordChanged(string value) => ApplyLocalFilter();
    partial void OnSelectedLineChanged(ProductColorLineModel? value) => Lines.SelectedItem = value;

    /// <summary>基类忙碌和权限状态变化时同步手柄可用状态，不额外维护一份门禁状态。</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(IsBusy) or nameof(IsReadOnly)) OnPropertyChanged(nameof(CanReorder));
    }

    /// <summary>未知技术异常只提供页面级通用反馈，不向用户暴露内部异常详情。</summary>
    protected override Task HandleExceptionAsync(Exception exception)
    {
        if (!_disposed)
        {
            SetValidationFeedback("产品颜色操作失败，请重试。");
            _messageService.ShowError("产品颜色操作失败", ValidationSummary!);
        }
        return Task.CompletedTask;
    }

    /// <summary>同步页面校验摘要与提示栏。</summary>
    private void SetValidationFeedback(string? message)
    {
        ValidationSummary = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        HasValidationError = ValidationSummary is not null;
    }

    /// <summary>解除页面订阅并阻止迟到读取回写；不取消已经发出的写请求。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        OnPropertyChanged(nameof(CanReorder));
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        Lines.ListStateChanged -= OnListStateChanged;
    }

    /// <summary>按页面白名单检查填充来源、目标归属及当前写入许可，拒绝外来行和危险字段。</summary>
    private bool CanFillDown(FillDownRequest? request) => CanWriteCommand()
        && request is not null && request.Fields.All(field => field is nameof(ProductColorLineModel.ColorCode)
            or nameof(ProductColorLineModel.ColorNameEn)
            or nameof(ProductColorLineModel.ColorNameZh))
        && request.TryGetRows<ProductColorLineModel>(Lines.Items, out _, out var targets)
        && targets.Any(row => !row.IsDeleted && !row.IsReadOnly);

    /// <summary>执行当前页面允许的向下填充；关系字段完整回填，保留模型通知与页面放弃修改基线。</summary>
    [RelayCommand(CanExecute = nameof(CanFillDown))]
    private void FillDown(FillDownRequest? request)
    {
        if (!CanFillDown(request) || !request!.TryGetRows<ProductColorLineModel>(Lines.Items, out var source, out var targets)) return;
        foreach (var target in targets.Where(row => !row.IsDeleted && !row.IsReadOnly))
        {
            foreach (var field in request.Fields)
            {
                switch (field)
                {
                    case nameof(ProductColorLineModel.ColorCode): target.ColorCode = source.ColorCode; break;
                    case nameof(ProductColorLineModel.ColorNameEn): target.ColorNameEn = source.ColorNameEn; break;
                    case nameof(ProductColorLineModel.ColorNameZh): target.ColorNameZh = source.ColorNameZh; break;
                }
            }
        }
    }
}
