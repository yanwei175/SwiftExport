using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using SwiftExport.Contracts.Apis.Inventory.Inbounds;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapster;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExportClient.Features.Inventory.Inbounds.Models;
using SwiftExportClient.Features.Inventory.Inbounds.Services;
using SwiftExportClient.Features.Inventory.WarehouseSites.Services;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.UI;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.Inventory.Inbounds.ViewModels;

/// <summary>入库管理查询、草稿持久化与当前单正向过账会话；不直接操作库存。</summary>
public sealed partial class InboundManagerViewModel : ViewModelBaseEx, IDocumentTabViewModel, IDocumentTabInitializable, IDisposable
{
    /// <summary>单实例管理页路由。</summary>
    public const string RouteKey = "inventory.inbound-manager";
    private readonly IWarehousePickerService _warehouses;
    private readonly IInboundSourceDialogService _sources;
    private readonly IDialogService _dialogs;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    /// <summary>注入选择器、查询和持久化服务；可选依赖支持设计时及本地模型测试。</summary>
    public InboundManagerViewModel(IWarehousePickerService warehouses, IInboundSourceDialogService sources, IDialogService dialogs,
        IInboundManagerApi? api = null, IApiCallRunner? runner = null,
        IInboundPersistenceService? persistence = null, IInboundDeleteDialogService? deleteDialogs = null,
        IInboundPostDialogService? postDialogs = null, SwiftExportClient.Authentication.ICurrentUserService? postingUser = null, IInboundReverseDialogService? reverseDialogs = null)
    {
        _warehouses = warehouses; _sources = sources; _dialogs = dialogs;
        _api = api; _runner = runner;
        _persistence = persistence; _deleteDialogs = deleteDialogs;
        _reverseDialogs = reverseDialogs;
        _postDialogs = postDialogs; _postingUser = postingUser;
        if (_postingUser is not null) _postingUser.CurrentUserChanged += OnPostingUserChanged;
        Title = "入库单管理";
        Headers.ListStateChanged += OnHeadersChanged;
        PropertyChanged += OnManagerPropertyChanged;
        RegisterBusyAwareCommand(AddNewHeaderRowCommand);
        RegisterBusyAwareCommand(DeleteSelectedCommand);
        RegisterBusyAwareCommand(DeleteSelectedLineCommand);
        RegisterBusyAwareCommand(OpenWarehousePickerCommand);
        RegisterBusyAwareCommand(OpenInboundSourceDialogCommand);
        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(PostCommand);
        RegisterBusyAwareCommand(ReverseCommand);
    }

    /// <summary>头行编辑集合，每个头保留自己的明细。</summary>
    public BaseListModel<InboundHeaderModel, InboundHeaderDetailDto> Headers { get; } = new(_ => new InboundHeaderModel());
    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty] public partial InboundDocumentTypeEnum SelectedDocumentType { get; set; } = InboundDocumentTypeEnum.PurchaseReceipt;
    [ObservableProperty] public partial InboundPostingStatusEnum SelectedPostingStatus { get; set; } = InboundPostingStatusEnum.Unposted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Lines))]
    [NotifyPropertyChangedFor(nameof(LinesReadOnly))]
    [NotifyPropertyChangedFor(nameof(HeaderReadOnly))]
    [NotifyCanExecuteChangedFor(nameof(OpenInboundSourceDialogCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedLineCommand))]
    [NotifyCanExecuteChangedFor(nameof(PostCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReverseCommand))]
    public partial InboundHeaderModel? SelectedItem { get; set; }
    [ObservableProperty] public partial string ResultSummary { get; set; } = "保存仅持久化草稿；正向过账后增加库存，原单不可编辑，可整单反过账。";

    /// <summary>当前单明细集合；切换不丢弃其他单据的本地修改。</summary>
    public BaseListModel<InboundLineModel, InboundLineDetailDto>? Lines => SelectedItem?.Lines;
    /// <summary>已过账、非活动或自动单据不允许编辑明细。</summary>
    public bool LinesReadOnly => SelectedItem?.CanEdit != true || IsBusy;
    /// <summary>单头普通编辑同样受草稿状态约束。</summary>
    public bool HeaderReadOnly => SelectedItem?.CanEdit != true || IsBusy;
    /// <summary>服务端统计与当前页面新增草稿分别展示。</summary>
    public string PageStatus => $"服务端共 {TotalCount} 张，第 {PageIndex} 页；本页新增 {Headers.AddedItems.Count} 张";
    /// <inheritdoc/>
    public string DocumentTitle => Title;
    /// <inheritdoc/>
    public bool HasUnsavedChanges => _pendingWrites.Count > 0 || Headers.HasChanges || Headers.AllLines.Any(x => x.Lines.HasChanges);

    private bool CanEdit() => !_disposed && IsNotBusy && !IsReadOnly;
    private bool CanEditLines() => CanEdit() && SelectedItem?.CanEdit == true;
    private bool CanPickWarehouse(InboundHeaderModel? row) => CanEdit() && row?.CanEdit == true;
    private bool BackendUnavailable() => false;

    /// <summary>建立本地采购草稿，正式编号由未来后端生成。</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddNewHeaderRow()
    {
        if (!CanEdit()) return;
        SelectedItem = Headers.AddNew(new InboundHeaderDetailDto { Uid = Guid.NewGuid(), Version = 0 });
    }

    /// <summary>删除当前人工草稿的勾选明细，保留基类删除与脏状态语义。</summary>
    [RelayCommand(CanExecute = nameof(CanEditLines))]
    private void DeleteSelectedLine()
    {
        if (!CanEditLines() || Lines is null) return;
        var rows = Lines.CheckedItems.Count > 0 ? Lines.CheckedItems : Lines.SelectedItem is null ? [] : new[] { Lines.SelectedItem };
        foreach (var row in rows.ToArray()) Lines.Remove(row);
    }

    /// <summary>仓库只回填打开弹窗时的单头；取消或释放时不改仓。</summary>
    [RelayCommand(CanExecute = nameof(CanPickWarehouse))]
    private async Task OpenWarehousePickerAsync(InboundHeaderModel? row)
    {
        if (row is null || !CanPickWarehouse(row)) return;
        await RunBusyAsync(async () =>
        {
            var result = await _warehouses.PickAsync(_lifetime.Token);
            if (_disposed || result is null || !result.IsActive || !Headers.Items.Contains(row) || !row!.CanEdit) return;
            row.WarehouseSiteId = result.Uid;
            row.WarehouseCodeSnapshot = result.Code ?? string.Empty;
            row.WarehouseNameSnapshot = result.Name ?? string.Empty;
        }, "选择收货仓库");
    }

    /// <summary>按当前类型选择来源，仓库由单头独立选择；确认后直接接纳快照，不发起二次详情或版本检查。</summary>
    [RelayCommand(CanExecute = nameof(CanEditLines))]
    private async Task OpenInboundSourceDialogAsync()
    {
        if (!CanEditLines()) return;
        var header = SelectedItem!;
        if (header.DocumentType != InboundDocumentTypeEnum.PurchaseReceipt) { ResultSummary = "该入库类型的选源尚未实现。"; return; }
        var type = header.DocumentType;
        await RunBusyAsync(async () =>
        {
            var rows = await _sources.PickAsync(new InboundSourcePickerRequest { DocumentType = type, CurrentInboundDocumentId = header.IsNew ? null : header.Uid }, _lifetime.Token);
            if (_disposed || rows is null || !Headers.Items.Contains(header) || !header.CanEdit || header.DocumentType != type) return;
            if (rows.Any(x => x.DocumentType != type || x.SourceLineId == Guid.Empty || x.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(x.Unit) || x.RemainingReceivableQty <= 0))
            { ResultSummary = "所选来源类型或标识不匹配，未加入明细。"; return; }
            var added = 0;
            using (header.Lines.SuppressListEvents())
                foreach (var source in rows)
                {
                    // 重复获取不覆盖用户已经填写的数量；不同源版本不在选源阶段判定冲突。
                    if (header.Lines.Items.Any(x => x.SourceLineId == source.SourceLineId)) continue;
                    var dto = source.Adapt<InboundLineDetailDto>();
                    dto.Uid = Guid.NewGuid(); dto.Version = 0; dto.InboundDocumentId = header.Uid;
                    dto.LineNumber = header.Lines.Items.Count == 0 ? 1 : header.Lines.Items.Max(x => x.LineNumber) + 1;
                    dto.Quantity = 0; // 实收必须由用户填写，不能自动把剩余量当成本次实收。
                    header.Lines.AddNew(dto); added++;
                }
            ResultSummary = $"已加入 {added} 行，重复来源已跳过；请填写本次入库数量，保存不会增加库存。";
        }, "选择入库来源");
    }

    [RelayCommand(CanExecute = nameof(BackendUnavailable))] private void AddNewLineRow() { }

    /// <summary>同步汇总与关闭脏状态；不将显示合计写回实体。</summary>
    private void OnHeadersChanged(object? sender, ListStateChangedEventArgs<InboundHeaderModel> e)
    {
        SynchronizeDetailSubscriptions();
        NotifyEditState();
    }

    /// <summary>头和明细共享页面级脏状态通知，明细编辑不依赖单头字段变更。</summary>
    private void NotifyEditState()
    {
        IsDirty = HasUnsavedChanges;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(PageStatus));
        OnPropertyChanged(nameof(LinesReadOnly));
        OnPropertyChanged(nameof(HeaderReadOnly));
        OpenWarehousePickerCommand.NotifyCanExecuteChanged();
        PostCommand.NotifyCanExecuteChanged();
        ReverseCommand.NotifyCanExecuteChanged();
        OpenInboundSourceDialogCommand.NotifyCanExecuteChanged();
        DeleteSelectedLineCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Busy 退出后同步只读绑定，避免详情加载完成后仍保留加载期间的只读值。</summary>
    private void OnManagerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IsBusy)) return;
        OnPropertyChanged(nameof(LinesReadOnly));
        OnPropertyChanged(nameof(HeaderReadOnly));
    }

    /// <inheritdoc/>
    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return false;
        if (_pendingWrites.Count > 0)
        { ResultSummary = "存在待确认命令或待回填详情，请选择对应单据点击刷新完成确认后再关闭。"; return false; }
        return !HasUnsavedChanges || await _dialogs.ConfirmAsync("关闭入库管理", "本地修改尚未保存，是否放弃并关闭？", cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    protected override Task HandleExceptionAsync(Exception exception)
    {
        System.Diagnostics.Trace.TraceError("入库操作失败：{0}", exception.GetType().Name);
        if (!_disposed) ResultSummary = "入库操作失败，请重试；已有编辑未清空。";
        return Task.CompletedTask;
    }

    /// <summary>关闭时解除事件并取消未完成的选择操作，禁止晚到结果回填。</summary>
    public void Dispose()
    {
        if (_postingUser is not null) _postingUser.CurrentUserChanged -= OnPostingUserChanged;
        if (_disposed) return;
        _disposed = true;
        Headers.ListStateChanged -= OnHeadersChanged;
        PropertyChanged -= OnManagerPropertyChanged;
        foreach (var row in _observedHeaders) row.DetailStateChanged -= OnDetailStateChanged;
        _observedHeaders.Clear();
        _readRequestId++;
        _readQuery?.Cancel();
        _lifetime.Cancel(); _lifetime.Dispose();
    }
}
