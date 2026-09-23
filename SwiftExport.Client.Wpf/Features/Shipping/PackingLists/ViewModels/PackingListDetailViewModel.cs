using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapster;
using SwiftExport.Contracts.Apis.Shipping;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExportClient.Features.Shipping.PackingLists.Models;
using SwiftExportClient.Features.Shipping.PackingLists.Services;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;

namespace SwiftExportClient.Features.Shipping.PackingLists.ViewModels;

/// <summary>拥有草稿编辑、保存及过账确认交互；实际库存事务由后端执行。</summary>
public sealed partial class PackingListDetailViewModel : ViewModelBaseEx, IDocumentTabViewModel, IDocumentTabInitializable, IDisposable
{
    private readonly IPackingListApi _api;
    private readonly IApiCallRunner _runner;
    private readonly IReferenceDataCacheService _cache;
    private readonly IPackingListSourcePickerService _sources;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly IMessageService _messages;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed, _initialized, _loading, _dialogOpen, _isNew, _loaded;
    private string? _saveFingerprint;
    private Guid _saveCommandId;
    private Guid _uid;
    private bool _lastSaveSucceeded;
    /// <summary>独立详情页路由，使用客户端单据 Uid 作为稳定页签身份。</summary>
    public const string RouteKey = "shipping.packing-list-detail";
    /// <summary>注入窄 API、参考缓存和 UI 边界，不直接创建视图。</summary>
    public PackingListDetailViewModel(IPackingListApi api, IApiCallRunner runner, IReferenceDataCacheService cache,
        IPackingListSourcePickerService sources, INavigationService navigation,
        IDialogService dialogs, IMessageService messages,
        IPackingListPostingDialogService? postingDialogs = null,
        SwiftExportClient.Authentication.ICurrentUserService? postingUser = null)
    {
        _api = api; _runner = runner; _cache = cache; _sources = sources;
        _navigation = navigation; _dialogs = dialogs; _messages = messages;
        _postingDialogs = postingDialogs; _postingUser = postingUser;
        if (_postingUser != null) _postingUser.CurrentUserChanged += OnPostingUserChanged;
        IsReadOnly = true;
        Header.PropertyChanged += OnHeaderChanged;
        Lines.ListStateChanged += OnLinesChanged;
        PropertyChanged += OnPageChanged;
        foreach (var command in new IRelayCommand[] { SaveCommand, SelectSourcesCommand, DeleteLineCommand,
            MoveLineUpCommand, MoveLineDownCommand, SplitTailCommand, UndoCommand })
            RegisterBusyAwareCommand(command);
    }
    public PackingListHeaderModel Header { get; } = new();
    public PackingListLineListModel Lines => Header.Lines;
    public ReadOnlyObservableCollection<CustomerDto> Customers => _cache.Customers;
    [ObservableProperty] public partial CustomerDto? SelectedCustomer { get; set; }
    [ObservableProperty] public partial PackingListLineModel? SelectedLine { get; set; }
    [ObservableProperty] public partial string ResultSummary { get; private set; } = "草稿保存以服务端来源额度复核结果为准。";
    /// <summary>仅成功加载的有效未过账草稿可编辑，弹窗或写入期间冻结。</summary>
    public bool IsEditable => !_disposed && _loaded && !IsReadOnly && !IsBusy && !_dialogOpen && !HasPendingPosting;
    /// <summary>只读单据仍可浏览、选行和复制；写入或弹窗期间暂停交互。</summary>
    public bool CanInteractWithLines => !_disposed && _loaded && !IsBusy && !_dialogOpen;
    /// <summary>有物理行后不允许换客户，避免来源跨客户。</summary>
    public bool CanEditFacts => IsEditable && Lines.AllLines.Count == 0;
    /// <inheritdoc/>
    public string DocumentTitle => _isNew ? "新建 PackingList" : Header.PackingListNo ?? "PackingList 详情";
    /// <inheritdoc/>
    public bool HasUnsavedChanges => _loaded && (Header.IsDirty || Lines.HasChanges || Lines.HasEmptyGroups);
    /// <summary>视图负责提交本地编辑器并检查 WPF 转换错误；不包含业务逻辑。</summary>
    public Func<bool>? CommitEdits { get; set; }
    /// <inheritdoc/>
    public async Task InitializeDocumentAsync(object? parameter, CancellationToken token)
    {
        if (_initialized) return;
        if (parameter is not PackingListOpenRequest request || request.Uid == Guid.Empty)
        { ReportValidationWarning("缺少有效的箱单打开参数。"); return; }
        _initialized = true; _uid = request.Uid; _isNew = request.IsNew;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await RunBusyAsync(async () =>
        {
            var cacheResult = await _cache.EnsureLoadedAsync();
            if (_disposed || linked.IsCancellationRequested) return;
            if (_isNew)
            {
                ApplyDetail(new PackingListDetailDto { Header = new PackingListHeaderDto { Uid = _uid, InvoiceDate = DateTime.Today } });
                Header.MarkAsAdded();
            }
            else await LoadAsync(linked.Token);
            if (!cacheResult.Success && !_disposed) ReportAttention("客户缓存未完整加载，请刷新客户资料后重试。");
        }, "加载箱单…");
    }
    /// <summary>加载失败不把现有单据误当成新建；重试只读初始化。</summary>
    [RelayCommand]
    private Task ReloadAsync() => _disposed || IsBusy || _dialogOpen || HasPendingPosting || HasUnsavedChanges || _isNew ? Task.CompletedTask :
        RunBusyAsync(() => LoadAsync(_lifetime.Token), "重新加载…");
    private async Task LoadAsync(CancellationToken token)
    {
        var result = await _runner.RunAsync(() => _api.GetAsync(_uid, token), token, "箱单读取失败");
        if (_disposed || token.IsCancellationRequested) return;
        if (!result.Success || !IsValidDetail(result.Data)) { ReportValidationWarning(result.Success ? "单据响应身份无效。" : result.Message); return; }
        ApplyDetail(result.Data!);
    }
    private bool IsValidDetail(PackingListDetailDto? detail) => detail?.Header?.Uid == _uid && detail.Lines is not null
        && detail.Lines.All(x => x.PackingListHeadId == _uid && x.Uid != Guid.Empty)
        && detail.Lines.Select(x => x.Uid).Distinct().Count() == detail.Lines.Count;
    private void ApplyDetail(PackingListDetailDto detail)
    {
        _loading = true;
        try
        {
            Header.Load(detail.Header); Lines.LoadFromDtos(detail.Lines.Where(x => !x.IsDeleted).OrderBy(x => x.LineNumber));
            SelectedCustomer = Customers.FirstOrDefault(x => x.Uid == Header.CustomerId);
            SelectedLine = null; SelectedPackingGroup = null; _loaded = true;
            IsReadOnly = detail.Header.IsDeleted || Header.LifecycleStatus != PackingListLifecycleStatusEnum.Active
                || Header.PostingStatus != PackingListPostingStatusEnum.Unposted;
            ValidationSummary = null;
        }
        finally { _loading = false; NotifyState(); }
    }
    partial void OnSelectedCustomerChanged(CustomerDto? value)
    {
        if (_loading || !CanEditFacts) return;
        Header.CustomerId = value?.Uid ?? Guid.Empty;
        Header.CustomerNameSnapshot = value?.CustomerName;
        Header.CustomerAddressSnapshot = value?.Address;
    }
    /// <summary>切换选中行时刷新当前草稿操作可用性。</summary>
    partial void OnSelectedLineChanged(PackingListLineModel? value) => NotifyState();
    /// <summary>保存前校验完整有效行，专用写 DTO 排除状态、审计和图片。</summary>
    [RelayCommand(CanExecute = nameof(IsEditable))]
    private async Task SaveAsync()
    {
        _lastSaveSucceeded = false;
        if (!IsEditable) return;
        if (CommitEdits?.Invoke() == false) { ReportValidationWarning("请先修正表格输入格式。"); return; }
        if (Lines.HasEmptyGroups) { ReportValidationWarning("请给空拼箱添加来源，或删除空组后保存。"); return; }
        if (Header.CustomerId == Guid.Empty || string.IsNullOrWhiteSpace(Header.CustomerNameSnapshot))
        { ReportValidationWarning("请确定客户及客户名称。"); return; }
        if (Header.InvoiceDate == default || Lines.Items.Count == 0) { ReportValidationWarning("请填写单据日期并至少添加一行来源。"); return; }
        var error = await Lines.ValidateAllAsync();
        if (error is not null) { ReportValidationWarning(error); return; }
        if (!IsEditable) return;
        if (Lines.Items.Any(x => x.GrossDemandId == Guid.Empty
            || (x.IsNonStandardSnapshot && (x.ProductId.HasValue))
            || (!x.IsNonStandardSnapshot && (!x.ProductId.HasValue || x.ProductId == Guid.Empty))))
        { ReportValidationWarning("来源的标准/非标产品身份不一致。"); return; }
        await RunBusyAsync(async () =>
        {
            Renumber();
            var command = new UpdatePackingListDraftCommand
            {
                ExpectedVersion = Header.Version,
                Header = Header.Adapt<PackingListDraftHeaderInput>(),
                Lines = Lines.Items.Adapt<List<PackingListDraftLineInput>>(),
                DeletedLines = Lines.DeletedItems.Where(x => !x.IsNew).Adapt<List<PackingListDeletedLineInput>>()
            };
            var fingerprint = JsonSerializer.Serialize(command);
            if (_saveFingerprint != fingerprint)
            {
                _saveFingerprint = fingerprint; _saveCommandId = Guid.NewGuid();
            }
            command.CommandId = _saveCommandId;
            var result = _isNew
                ? await _runner.RunAsync(() => _api.CreateAsync(new CreatePackingListDraftCommand
                {
                    CommandId = _saveCommandId,
                    Uid = _uid,
                    Header = command.Header,
                    Lines = command.Lines
                }), "保存失败")
                : await _runner.RunAsync(() => _api.UpdateAsync(_uid, command), "保存失败");

            if (_disposed) return;
            if (!result.Success || result.Data?.Uid != _uid || !IsValidDetail(result.Data.Detail))
            {
                ReportValidationWarning(result.Success ? "保存响应不完整，编辑内容已保留。" : result.Message); return;
            }
            _isNew = false; _saveFingerprint = null; ApplyDetail(result.Data.Detail);
            _lastSaveSucceeded = true;
            ResultSummary = result.Data.AlreadyApplied ? "已接纳此前成功保存的结果。" : "草稿已保存。";
        }, "保存箱单…");
    }
    /// <summary>同来源可多次选入，物理行身份始终独立；只接纳相同客户来源。</summary>
    [RelayCommand(CanExecute = nameof(IsEditable))]
    private async Task SelectSourcesAsync()
    {
        if (!IsEditable) return;
        if (Header.CustomerId == Guid.Empty) { ReportValidationWarning("请先选择客户。"); return; }
        if (CommitEdits?.Invoke() == false || Lines.Items.Any(x => x.Qty <= 0))
        { ReportValidationWarning("请先修正草稿数量及输入格式，再获取来源。"); return; }
        _dialogOpen = true; NotifyState();
        try
        {
            var draft = Lines.Items.GroupBy(x => x.GrossDemandId).ToDictionary(x => x.Key, x => x.Sum(y => y.Qty));
            var selected = await _sources.PickAsync(Header.CustomerId, draft, _lifetime.Token);
            if (_disposed || selected is null) return;
            if (selected.Any(x => x.Source.CustomerId != Header.CustomerId || x.Qty <= 0
                || x.Source.GrossDemandId == Guid.Empty
                || (x.Source.IsNonStandard && x.Source.ProductId.HasValue)
                || (!x.Source.IsNonStandard && (!x.Source.ProductId.HasValue || x.Source.ProductId == Guid.Empty))))
            { ReportValidationWarning("选源返回身份或数量异常。"); return; }
            foreach (var selection in selected)
            {
                var dto = selection.Source.Adapt<PackingListLineDto>();
                dto.Uid = Guid.NewGuid(); dto.PackingListHeadId = _uid; dto.LineNumber = Lines.Items.Count + 1; dto.Qty = selection.Qty;
                // 箱数与小计由行模型实时计算。
                SelectedLine = Lines.AddNew(dto);
                if (SelectedPackingGroup is { } group)
                {
                    using (Lines.SuppressListEvents())
                    {
                        SelectedLine.PackingGroupId = group.Id;
                        SelectedLine.PcsPerPackage = SelectedLine.Qty;
                        SelectedLine.Marks = group.Marks;
                        Lines.ClearMetrics(group.Id);
                        PlaceInGroup(SelectedLine, group.Id);
                        Renumber();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { if (!_disposed) await HandleExceptionAsync(ex); }
        finally { _dialogOpen = false; NotifyState(); }
    }

    [RelayCommand(CanExecute = nameof(IsEditable))]
    private void DeleteLine()
    {
        if (!IsEditable || CommitEdits?.Invoke() == false) return;
        using (Lines.SuppressListEvents())
        {
            if (SelectedLine?.PackingGroupId is { } id) Lines.ClearMetrics(id);
            Lines.Remove(SelectedLine); SelectedLine = null; Renumber();
        }
    }

    [RelayCommand(CanExecute = nameof(CanMoveLineUp))]
    private void MoveLineUp(PackingListLineModel? line) => MoveLine(line ?? SelectedLine, -1);
    private bool CanMoveLineUp(PackingListLineModel? line)
    {
        if (!IsEditable)
            return false;

        line ??= SelectedLine;

        if (line is null) return false;
        var index = Lines.Items.IndexOf(line);
        return index > 0 && (!line.PackingGroupId.HasValue || Lines.Items[index - 1].PackingGroupId == line.PackingGroupId);
    }

    [RelayCommand(CanExecute = nameof(CanMoveLineDown))]
    private void MoveLineDown(PackingListLineModel? line) => MoveLine(line ?? SelectedLine, 1);
    private bool CanMoveLineDown(PackingListLineModel? line)
    {
        if (!IsEditable)
            return false;

        line ??= SelectedLine;

        if (line is null)
            return false;

        var index = Lines.Items.IndexOf(line);

        return index >= 0 && index < Lines.Items.Count - 1
            && (!line.PackingGroupId.HasValue || Lines.Items[index + 1].PackingGroupId == line.PackingGroupId);
    }



    private void MoveLine(PackingListLineModel? line, int offset)
    {
        if (!IsEditable || line is null)
            return;

        if (CommitEdits?.Invoke() == false) return;
        MovePackingBlock(line, offset);
        Renumber();

        MoveLineUpCommand.NotifyCanExecuteChanged();
        MoveLineDownCommand.NotifyCanExecuteChanged();
    }




    /// <summary>拆尾箱只改当前草稿；来源和总数量不变，新增行使用独立身份。</summary>
    [RelayCommand(CanExecute = nameof(CanSplitTail))]
    private void SplitTail(PackingListLineModel? line)
    {
        if (!IsEditable || line is null || !Lines.Items.Contains(line)) return;
        if (CommitEdits?.Invoke() == false) { ReportValidationWarning("请先修正表格输入格式。"); return; }
        if (!CanSplitTail(line)) return;
        var remainder = line.Qty % line.PcsPerPackage!.Value;
        var fullQty = line.Qty - remainder;
        if (fullQty == 0)
        {
            // 本身不足一箱时直接转换为尾箱，避免制造零数量行。
            line.PcsPerPackage = line.Qty;
            line.NwPerPackage = null; line.GwPerPackage = null; line.CbmPerPackage = null;
            SelectedLine = line;
            ReportAttention("尾箱已拆分，请填写尾箱的单箱净重、毛重和体积。");
            return;
        }
        var index = Lines.Items.IndexOf(line);
        var dto = line.ToDto();
        dto.Uid = Guid.NewGuid(); dto.Version = 0; dto.IsDeleted = false;
        dto.CreatedBy = null; dto.UpdatedBy = null; dto.CreatedDate = default; dto.UpdatedDate = default;
        dto.Qty = remainder; dto.PcsPerPackage = remainder; dto.PackageCount = 1;
        dto.NwPerPackage = null; dto.GwPerPackage = null; dto.CbmPerPackage = null;
        dto.SubTotalNw = null; dto.SubTotalGw = null; dto.SubTotalCbm = null;
        line.Qty = fullQty;
        var tail = Lines.AddNew(dto);
        Lines.Move(tail, index + 1); Renumber(); SelectedLine = tail;
        ReportAttention("尾箱已拆分，请填写尾箱的单箱净重、毛重和体积。");
    }
    private bool CanSplitTail(PackingListLineModel? line) =>
        IsEditable && line is not null && Lines.Items.Contains(line) && line.CanSplitTail;

    private void Renumber()
    {
        for (var i = 0; i < Lines.Items.Count; i++) Lines.Items[i].LineNumber = i + 1;
    }



    /// <summary>撤销到上次服务器快照；新建页撤销只清空新增行。</summary>
    [RelayCommand(CanExecute = nameof(IsEditable))]
    private async Task UndoAsync()
    {
        if (!IsEditable) return;
        _dialogOpen = true; NotifyState();
        try
        {
            if (!await _dialogs.ConfirmAsync("撤销修改", "放弃本页尚未保存的修改？") || _disposed) return;
            _loading = true;
            try { Header.RejectChanges(); Lines.RejectChanges(); SelectedPackingGroup = null; SelectedCustomer = Customers.FirstOrDefault(x => x.Uid == Header.CustomerId); if (_isNew) Header.MarkAsAdded(); }
            finally { _loading = false; }
        }
        finally { _dialogOpen = false; NotifyState(); }
    }
    [RelayCommand] private Task CloseAsync() => _navigation.CloseAsync(_navigation.OpenTabs.FirstOrDefault(x => ReferenceEquals(x.ContentViewModel, this)));
    /// <inheritdoc/>
    public async Task<bool> CanCloseAsync(CancellationToken token)
    {
        if (IsBusy || _dialogOpen || HasPendingPosting) return false;
        // 提交编辑器可能刚刚产生脏状态，必须先提交再决定是否需要关闭确认。
        var editorsValid = CommitEdits?.Invoke() != false;
        if (!HasUnsavedChanges && editorsValid) return true;
        _dialogOpen = true; NotifyState();
        try { return await _dialogs.ConfirmAsync("未保存修改", "当前箱单尚未保存，确定放弃并关闭？", cancellationToken: token); }
        finally { _dialogOpen = false; NotifyState(); }
    }
    private void OnHeaderChanged(object? sender, PropertyChangedEventArgs e) { if (!_loading) NotifyState(); }
    private void OnLinesChanged(object? sender, ListStateChangedEventArgs<PackingListLineModel> e) { if (!_loading) NotifyState(); }
    private void OnPageChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(IsBusy)) NotifyState(); }
    private void NotifyState()
    {
        IsDirty = HasUnsavedChanges;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(DocumentTitle));
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(CanEditFacts));
        OnPropertyChanged(nameof(CanInteractWithLines));
        foreach (var command in new IRelayCommand[] { SaveCommand, SelectSourcesCommand, DeleteLineCommand, MoveLineUpCommand,
            MoveLineDownCommand, SplitTailCommand, UndoCommand, PostCommand, ReverseCommand, TraceCommand,
            AddPackingGroupCommand, DeletePackingGroupCommand, SelectOrdinaryPackingCommand,
            MoveIntoPackingGroupCommand, MoveOutOfPackingGroupCommand, MovePackingGroupUpCommand, MovePackingGroupDownCommand }) command.NotifyCanExecuteChanged();
    }
    /// <summary>阻断性错误同时保留页面摘要并提示一次；释放后的页面不再发出通知。</summary>
    private void ReportValidationWarning(string? message)
    {
        if (_disposed) return;
        ValidationSummary = string.IsNullOrWhiteSpace(message) ? "操作未完成，请检查后重试。" : message;
        _messages.ShowWarning("PackingList", ValidationSummary);
    }

    /// <summary>需用户留意的非阻断信息保留在状态栏，并以警告提醒。</summary>
    private void ReportAttention(string message)
    {
        if (_disposed) return;
        ResultSummary = message;
        _messages.ShowWarning("PackingList", message);
    }

    /// <summary>保持用户输入，仅反馈稳定错误文本。</summary>
    protected override Task HandleExceptionAsync(Exception exception)
    { System.Diagnostics.Trace.TraceError("PackingList详情异常：{0}", exception.GetType().Name); ReportValidationWarning("操作失败，当前编辑内容已保留。"); return Task.CompletedTask; }
    /// <summary>取消只读请求、解除事件和视图提交委托；写请求不受查询取消影响。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); _lifetime.Dispose(); CommitEdits = null;
        Header.PropertyChanged -= OnHeaderChanged; Lines.ListStateChanged -= OnLinesChanged; PropertyChanged -= OnPageChanged;
        if (_postingUser != null) _postingUser.CurrentUserChanged -= OnPostingUserChanged;
    }
}
