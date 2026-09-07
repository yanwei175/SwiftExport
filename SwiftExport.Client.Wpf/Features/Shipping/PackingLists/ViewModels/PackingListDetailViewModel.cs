using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.Shipping;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExportClient.Features.Shipping.PackingLists.Models;
using SwiftExportClient.Features.Shipping.PackingLists.Services;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SwiftExportClient.Features.Shipping.PackingLists.ViewModels;

public sealed partial class PackingListDetailViewModel : ViewModelBaseEx, IDocumentTabViewModel, IDocumentTabInitializable, IDisposable
{
    public const string RouteKey = "shipping-packing-list-detail";
    private readonly IPackingListWorkspaceApi _api;
    private readonly IApiCallRunner _runner;
    private readonly IDialogService _dialogs;
    private readonly IMessageService _messages;
    private readonly IPackingListDocumentOutputService _output;
    private CancellationTokenSource? _sourceCancellation;
    private bool _loading;

    public PackingListDetailViewModel(IPackingListWorkspaceApi api, IApiCallRunner runner, IDialogService dialogs, IMessageService messages, IPackingListDocumentOutputService output)
    {
        _api = api; _runner = runner; _dialogs = dialogs; _messages = messages; _output = output; Title = "PackingList 详情";
        foreach (var command in new IRelayCommand[] { SaveCommand, LoadSourcesCommand, AddSourceCommand, RemoveLineCommand, PreviewPostCommand, PostCommand, PreviewReverseCommand, ReverseCommand, TraceCommand, ExportCommand, PrintCommand }) RegisterBusyAwareCommand(command);
    }

    public ObservableCollection<PackingListLineModel> Lines { get; } = [];
    public ObservableCollection<PackingListSourceDto> Sources { get; } = [];
    public ObservableCollection<PackingListProductSummaryDto> ProductSummary { get; } = [];
    [ObservableProperty] public partial Guid DocumentId { get; private set; }
    [ObservableProperty] public partial int Version { get; private set; }
    [ObservableProperty] public partial Guid CustomerId { get; private set; }
    [ObservableProperty] public partial string? CustomerName { get; private set; }
    [ObservableProperty] public partial string? PackingListNo { get; set; }
    [ObservableProperty] public partial string? InvoiceNo { get; set; }
    [ObservableProperty] public partial string? ContainerNo { get; set; }
    [ObservableProperty] public partial string? SealNo { get; set; }
    [ObservableProperty] public partial string? ShippingMark { get; set; }
    [ObservableProperty] public partial string? DeliveryAddress { get; set; }
    [ObservableProperty] public partial DateTime PackingDate { get; set; }
    [ObservableProperty] public partial DateTime ShipmentDate { get; set; }
    [ObservableProperty] public partial DateTime? Etd { get; set; }
    [ObservableProperty] public partial DateTime? Eta { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsDraft))] [NotifyCanExecuteChangedFor(nameof(SaveCommand))] [NotifyCanExecuteChangedFor(nameof(PreviewPostCommand))] [NotifyCanExecuteChangedFor(nameof(PostCommand))] [NotifyCanExecuteChangedFor(nameof(PreviewReverseCommand))] [NotifyCanExecuteChangedFor(nameof(ReverseCommand))] public partial PackingListPostingStatusEnum PostingStatus { get; private set; }
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(RemoveLineCommand))] public partial PackingListLineModel? SelectedLine { get; set; }
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(AddSourceCommand))] public partial PackingListSourceDto? SelectedSource { get; set; }
    [ObservableProperty] public partial string? SourceKeyword { get; set; }
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(PostCommand))] [NotifyCanExecuteChangedFor(nameof(ReverseCommand))] public partial PackingListPreviewDto? Preview { get; private set; }
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(PostCommand))] [NotifyCanExecuteChangedFor(nameof(ReverseCommand))] public partial string? PreviewToken { get; private set; }
    [ObservableProperty] public partial string? ResultSummary { get; private set; }

    public string DocumentTitle => string.IsNullOrWhiteSpace(PackingListNo) ? "PackingList" : PackingListNo;
    public bool HasUnsavedChanges => IsDirty;
    public bool IsDraft => PostingStatus == PackingListPostingStatusEnum.Unposted;

    public async Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (parameter is not Guid uid || uid == Guid.Empty) throw new ArgumentException("PackingList 文档参数无效。");
        DocumentId = uid; await LoadAsync(cancellationToken);
    }

    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
        => !IsDirty || await _dialogs.ConfirmAsync("未保存更改", "PackingList 草稿有未保存更改，仍要关闭吗？", "关闭", "取消", cancellationToken);

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task SaveAsync() => await RunWriteAsync(async () => await SaveDraftCoreAsync(), "正在保存 PackingList 草稿...");

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task LoadSourcesAsync()
    {
        _sourceCancellation?.Cancel(); _sourceCancellation?.Dispose(); _sourceCancellation = new CancellationTokenSource(); var token = _sourceCancellation.Token;
        await RunBusyAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.SearchSourcesAsync(new PackingListSourceSearchRequest { CustomerId = CustomerId, Keyword = SourceKeyword, PageIndex = 1, PageSize = 100 }, token), "查询来源失败");
            if (token.IsCancellationRequested) return; Sources.Clear(); if (result.Success && result.Data is not null) foreach (var item in result.Data.Items.Where(x => Lines.All(y => y.GrossDemandId != x.GrossDemandId))) Sources.Add(item);
            ResultSummary = result.Success && result.Data is not null ? $"可加入来源 {result.Data.TotalCount} 条。" : result.Message;
        }, "正在加载可发货来源...", false);
    }

    [RelayCommand(CanExecute = nameof(CanAddSource))]
    private void AddSource()
    {
        if (SelectedSource is null || SelectedSource.CustomerId != CustomerId || Lines.Any(x => x.GrossDemandId == SelectedSource.GrossDemandId)) return;
        AddLine(PackingListLineModel.FromSource(SelectedSource)); Sources.Remove(SelectedSource); SelectedSource = null; MarkChanged(); NotifyLineCommands();
    }

    [RelayCommand(CanExecute = nameof(CanRemoveLine))]
    private void RemoveLine() { if (SelectedLine is null) return; SelectedLine.PropertyChanged -= OnLineChanged; Lines.Remove(SelectedLine); SelectedLine = null; MarkChanged(); RebuildProductSummary(); NotifyLineCommands(); }

    [RelayCommand(CanExecute = nameof(CanPreviewPost))]
    private async Task PreviewPostAsync()
    {
        await RunWriteAsync(async () =>
        {
            if (!await SaveDraftCoreAsync()) return;
            var result = await _runner.RunAsync(() => _api.PostPreviewAsync(DocumentId, new PackingListPreviewCommand { ExpectedVersion = Version }), "过账预览失败");
            Preview = result.Data; PreviewToken = result.Data?.PreviewToken; ResultSummary = result.Data is null ? result.Message : result.Data.HasBlockers ? string.Join("；", result.Data.Blockers.Select(x => x.Message)) : "预览通过，可提交过账。";
        }, "正在生成过账预览...");
    }

    [RelayCommand(CanExecute = nameof(CanPost))]
    private async Task PostAsync()
    {
        var preview = Preview!;
        if (preview.HasBlockers) return;
        var warnings = preview.Warnings.Where(x => x.RequiresConfirmation).ToList();
        if (warnings.Count > 0 && !await _dialogs.ConfirmAsync("确认发货提示", string.Join(Environment.NewLine, warnings.Select(x => $"{x.Code}：{x.Message}")), "确认并过账", "取消")) return;
        await RunWriteAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.PostAsync(DocumentId, new PackingListPostCommand { CommandId = Guid.NewGuid(), ExpectedVersion = Version, PreviewToken = PreviewToken, ConfirmedWarningCodes = warnings.Select(x => x.Code).Distinct().ToList() }), "PackingList 过账失败");
            ResultSummary = result.Data?.Message ?? result.Message; if (result.Data?.Status is PackingListCommandStatusEnum.Success or PackingListCommandStatusEnum.AlreadyApplied) { _messages.ShowSuccess("PackingList", ResultSummary); await LoadAsync(CancellationToken.None); }
        }, "正在提交并过账...");
    }

    [RelayCommand(CanExecute = nameof(CanPreviewReverse))]
    private async Task PreviewReverseAsync()
    {
        await RunWriteAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.ReversePreviewAsync(DocumentId, new PackingListReverseCommand { ExpectedVersion = Version }), "反过账预览失败");
            Preview = result.Data; PreviewToken = result.Data?.PreviewToken; ResultSummary = result.Data is null ? result.Message : result.Data.HasBlockers ? string.Join("；", result.Data.Blockers.Select(x => x.Message)) : "反过账预览通过。";
        }, "正在生成反过账预览...");
    }

    [RelayCommand(CanExecute = nameof(CanReverse))]
    private async Task ReverseAsync()
    {
        if (!await _dialogs.ConfirmAsync("反过账", "将先反向全部出库，再反向自动入库。确定继续吗？", "反过账", "取消")) return;
        await RunWriteAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.ReverseAsync(DocumentId, new PackingListReverseCommand { CommandId = Guid.NewGuid(), ExpectedVersion = Version, PreviewToken = PreviewToken, Reason = "PackingList 工作台整体反过账" }), "PackingList 反过账失败");
            ResultSummary = result.Data?.Message ?? result.Message; if (result.Data?.Status is PackingListCommandStatusEnum.Success or PackingListCommandStatusEnum.AlreadyApplied) { _messages.ShowSuccess("PackingList", ResultSummary); await LoadAsync(CancellationToken.None); }
        }, "正在反过账...");
    }

    [RelayCommand]
    private async Task TraceAsync()
    {
        var result = await _runner.RunAsync(() => _api.GetTraceAsync(DocumentId), "加载 Trace 失败");
        if (result.Success && result.Data is not null) await _dialogs.ShowContentAsync("PackingList Trace", string.Join(Environment.NewLine, result.Data.Items.Select(x => $"{x.Stage} · {x.No} · {x.Description} · {x.Quantity:0.####}")));
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var result = await _runner.RunAsync(() => _api.GetExportAsync(DocumentId), "加载导出数据失败");
        if (result.Success && result.Data is not null) { var file = await _output.ExportAsync(result.Data); if (file is not null) _messages.ShowSuccess("导出完成", file); }
    }

    [RelayCommand]
    private async Task PrintAsync()
    {
        var result = await _runner.RunAsync(() => _api.GetExportAsync(DocumentId), "加载打印数据失败");
        if (result.Success && result.Data is not null) await _output.PrintAsync(result.Data);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(() => _api.GetDetailAsync(DocumentId, cancellationToken), "加载 PackingList 失败");
        if (!result.Success || result.Data?.Header is null) { ResultSummary = result.Message; return; }
        _loading = true;
        try
        {
            var h = result.Data.Header; Version = h.Version; CustomerId = h.CustomerId; CustomerName = h.CustomerName; PackingListNo = h.PackingListNo; InvoiceNo = h.InvoiceNo;
            ContainerNo = h.ContainerNo; SealNo = h.SealNo; ShippingMark = h.ShippingMark; DeliveryAddress = h.DeliveryAddressSnapshot; PackingDate = h.PackingDate;
            ShipmentDate = h.ShipmentDate; Etd = h.Etd; Eta = h.Eta; PostingStatus = h.PostingStatus; IsReadOnly = h.PostingStatus != PackingListPostingStatusEnum.Unposted || h.IsLegacyReadOnly;
            Lines.Clear(); foreach (var dto in result.Data.Lines) AddLine(PackingListLineModel.FromDetail(dto));
            Preview = null; PreviewToken = null; AcceptChanges(); RebuildProductSummary(); OnPropertyChanged(nameof(DocumentTitle)); OnPropertyChanged(nameof(IsDraft));
        }
        finally { _loading = false; }
    }

    private async Task<bool> SaveDraftCoreAsync()
    {
        if (!IsDraft || Lines.Count == 0) { ResultSummary = "草稿至少需要一条来源贡献。"; return false; }
        var result = await _runner.RunAsync(() => _api.UpdateDraftAsync(DocumentId, new UpdatePackingListDraftCommand
        {
            CommandId = Guid.NewGuid(), ExpectedVersion = Version,
            Header = new PackingListHeaderCommand { PackingListNo = PackingListNo, CustomerId = CustomerId, PackingDate = PackingDate, ShipmentDate = ShipmentDate, Etd = Etd, Eta = Eta, InvoiceNo = InvoiceNo, ContainerNo = ContainerNo, SealNo = SealNo, ShippingMark = ShippingMark, DeliveryAddressSnapshot = DeliveryAddress },
            Lines = Lines.Select(x => x.ToCommand()).ToList()
        }), "保存 PackingList 草稿失败");
        ResultSummary = result.Data?.Message ?? result.Message;
        if (result.Data?.Status is not (PackingListCommandStatusEnum.Success or PackingListCommandStatusEnum.AlreadyApplied)) return false;
        await LoadAsync(CancellationToken.None); return true;
    }

    private void AddLine(PackingListLineModel line) { line.PropertyChanged += OnLineChanged; Lines.Add(line); }
    private void OnLineChanged(object? sender, PropertyChangedEventArgs e) { if (!_loading) { MarkChanged(); RebuildProductSummary(); } }
    private void MarkChanged() { IsDirty = true; Preview = null; PreviewToken = null; ResultSummary = "内容已修改，原预览已失效。"; }
    private void RebuildProductSummary()
    {
        ProductSummary.Clear(); foreach (var group in Lines.GroupBy(x => new { x.ProductId, x.Sku, x.CustomerSku, x.Description, x.Unit })) ProductSummary.Add(new PackingListProductSummaryDto { ProductId = group.Key.ProductId, Sku = group.Key.Sku, CustomerSku = group.Key.CustomerSku, Description = group.Key.Description, Unit = group.Key.Unit, Quantity = group.Sum(x => x.Quantity), PackageCount = group.Sum(x => x.PackageCount ?? 0), NetWeight = group.Sum(x => x.NetWeight ?? 0), GrossWeight = group.Sum(x => x.GrossWeight ?? 0), Volume = group.Sum(x => x.Volume ?? 0) });
    }

    private void NotifyLineCommands() { SaveCommand.NotifyCanExecuteChanged(); PreviewPostCommand.NotifyCanExecuteChanged(); RemoveLineCommand.NotifyCanExecuteChanged(); }

    partial void OnPackingListNoChanged(string? value) { if (!_loading) MarkChanged(); }
    partial void OnInvoiceNoChanged(string? value) { if (!_loading) MarkChanged(); }
    partial void OnContainerNoChanged(string? value) { if (!_loading) MarkChanged(); }
    partial void OnSealNoChanged(string? value) { if (!_loading) MarkChanged(); }
    partial void OnShippingMarkChanged(string? value) { if (!_loading) MarkChanged(); }
    partial void OnDeliveryAddressChanged(string? value) { if (!_loading) MarkChanged(); }
    partial void OnPackingDateChanged(DateTime value) { if (!_loading) MarkChanged(); }
    partial void OnShipmentDateChanged(DateTime value) { if (!_loading) MarkChanged(); }
    partial void OnEtdChanged(DateTime? value) { if (!_loading) MarkChanged(); }
    partial void OnEtaChanged(DateTime? value) { if (!_loading) MarkChanged(); }
    private bool CanEdit() => IsNotBusy && IsDraft && !IsReadOnly;
    private bool CanAddSource() => CanEdit() && SelectedSource is not null;
    private bool CanRemoveLine() => CanEdit() && SelectedLine is not null && Lines.Count > 1;
    private bool CanPreviewPost() => CanEdit() && Lines.Count > 0;
    private bool CanPost() => CanEdit() && Preview is { IsReverse: false, HasBlockers: false } && !string.IsNullOrWhiteSpace(PreviewToken);
    private bool CanPreviewReverse() => IsNotBusy && PostingStatus == PackingListPostingStatusEnum.Posted;
    private bool CanReverse() => CanPreviewReverse() && Preview is { IsReverse: true, HasBlockers: false } && !string.IsNullOrWhiteSpace(PreviewToken);
    public void Dispose() { _sourceCancellation?.Cancel(); _sourceCancellation?.Dispose(); foreach (var line in Lines) line.PropertyChanged -= OnLineChanged; }
}
