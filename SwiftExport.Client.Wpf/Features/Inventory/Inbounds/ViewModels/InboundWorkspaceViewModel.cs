using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using System.Collections.ObjectModel;

namespace SwiftExportClient.Features.Inventory.Inbounds.ViewModels;

public sealed partial class InboundWorkspaceViewModel : ViewModelBaseEx, IAsyncInitializable
{
    public const string RouteKey = "inventory-inbounds";
    private readonly IInboundWorkspaceApi _api;
    private readonly IApiCallRunner _runner;
    private bool _loaded;
    private string? _postPreviewToken;
    private string? _reversePreviewToken;
    public ObservableCollection<InboundListItemDto> Items { get; } = [];
    public ObservableCollection<InboundSourceDto> Sources { get; } = [];
    public InboundWorkspaceViewModel(IInboundWorkspaceApi api, IApiCallRunner runner) { _api = api; _runner = runner; Title = "入库工作台"; RegisterBusyAwareCommand(SearchCommand); RegisterBusyAwareCommand(LoadSourcesCommand); RegisterBusyAwareCommand(CreateDraftCommand); RegisterBusyAwareCommand(PreviewPostCommand); RegisterBusyAwareCommand(PostCommand); RegisterBusyAwareCommand(PreviewReverseCommand); RegisterBusyAwareCommand(ReverseCommand); }
    [ObservableProperty] public partial string? Keyword { get; set; }
    [ObservableProperty] public partial InboundDocumentTypeEnum SelectedDocumentType { get; set; } = InboundDocumentTypeEnum.PurchaseReceipt;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(CreateDraftCommand))] [NotifyCanExecuteChangedFor(nameof(PreviewPostCommand))] [NotifyCanExecuteChangedFor(nameof(PostCommand))] [NotifyCanExecuteChangedFor(nameof(PreviewReverseCommand))] [NotifyCanExecuteChangedFor(nameof(ReverseCommand))] public partial InboundListItemDto? SelectedItem { get; set; }
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(CreateDraftCommand))] public partial InboundSourceDto? SelectedSource { get; set; }
    [ObservableProperty] public partial string? ResultSummary { get; set; }
    [ObservableProperty] public partial string? PreviewSummary { get; set; }
    public IReadOnlyList<InboundDocumentTypeEnum> DocumentTypes { get; } = [InboundDocumentTypeEnum.PurchaseReceipt, InboundDocumentTypeEnum.CustomerSuppliedReceipt];
    public Task InitializeAsync() { if (_loaded) return Task.CompletedTask; _loaded = true; return SearchAsync(); }
    [RelayCommand] private async Task SearchAsync() => await RunBusyAsync(async () => { var r = await _runner.RunAsync(() => _api.SearchAsync(new InboundSearchRequest { Keyword = Keyword, PageIndex = 1, PageSize = 100 }), "查询入库单失败"); Items.Clear(); if (r.Success && r.Data is not null) foreach (var item in r.Data.Items) Items.Add(item); ResultSummary = r.Success && r.Data is not null ? $"共 {r.Data.TotalCount} 张入库单。" : r.Message; }, "正在查询入库单...", false);
    [RelayCommand] private async Task LoadSourcesAsync() => await RunBusyAsync(async () => { var r = await _runner.RunAsync(() => _api.SearchSourcesAsync(new InboundSourceSearchRequest { DocumentType = SelectedDocumentType, Keyword = Keyword, PageIndex = 1, PageSize = 100 }), "查询来源失败"); Sources.Clear(); if (r.Success && r.Data is not null) foreach (var item in r.Data.Items) Sources.Add(item); ResultSummary = r.Success && r.Data is not null ? $"可收货来源 {r.Data.TotalCount} 条。" : r.Message; }, "正在加载可收货来源...", false);
    [RelayCommand(CanExecute = nameof(CanCreateDraft))] private async Task CreateDraftAsync() { var source = SelectedSource!; await RunWriteAsync(async () => { var r = await _runner.RunAsync(() => _api.CreateDraftAsync(new CreateInboundDraftCommand { CommandId = Guid.NewGuid(), DocumentType = SelectedDocumentType, ReceiptDate = DateTime.Today, Lines = [new InboundDraftLineCommand { SourceUid = source.SourceUid, ExpectedSourceVersion = source.SourceVersion, ExpectedRemainingQty = source.RemainingReceivableQty, Quantity = source.RemainingReceivableQty }] }), "创建入库草稿失败"); ResultSummary = r.Message; await SearchAsync(); }, "正在创建入库草稿..."); }
    [RelayCommand(CanExecute = nameof(CanOperate))] private async Task PreviewPostAsync() { var item = SelectedItem!; var r = await _runner.RunAsync(() => _api.PostPreviewAsync(item.Uid, new InboundPostCommand { ExpectedVersion = item.Version }), "过账预览失败"); _postPreviewToken = r.Data?.PreviewToken; PreviewSummary = r.Success && r.Data is not null ? (r.Data.HasBlockers ? string.Join("；", r.Data.Blockers) : "可提交过账。") : r.Message; }
    [RelayCommand(CanExecute = nameof(CanOperate))] private async Task PostAsync() { var item = SelectedItem!; await RunWriteAsync(async () => { var r = await _runner.RunAsync(() => _api.PostAsync(item.Uid, new InboundPostCommand { CommandId = Guid.NewGuid(), ExpectedVersion = item.Version, PreviewToken = _postPreviewToken }), "入库过账失败"); ResultSummary = r.Message; _postPreviewToken = null; await SearchAsync(); }, "正在入库过账..."); }
    [RelayCommand(CanExecute = nameof(CanOperate))] private async Task PreviewReverseAsync() { var item = SelectedItem!; var r = await _runner.RunAsync(() => _api.ReversePreviewAsync(item.Uid, new InboundReverseCommand { ExpectedVersion = item.Version }), "反过账预览失败"); _reversePreviewToken = r.Data?.PreviewToken; PreviewSummary = r.Success && r.Data is not null ? (r.Data.HasBlockers ? string.Join("；", r.Data.Blockers) : "可提交反过账。") : r.Message; }
    [RelayCommand(CanExecute = nameof(CanOperate))] private async Task ReverseAsync() { var item = SelectedItem!; await RunWriteAsync(async () => { var r = await _runner.RunAsync(() => _api.ReverseAsync(item.Uid, new InboundReverseCommand { CommandId = Guid.NewGuid(), ExpectedVersion = item.Version, PreviewToken = _reversePreviewToken, Reason = "工作台反过账" }), "入库反过账失败"); ResultSummary = r.Message; _reversePreviewToken = null; await SearchAsync(); }, "正在反过账..."); }
    private bool CanCreateDraft() => IsNotBusy && SelectedSource is not null;
    private bool CanOperate() => IsNotBusy && SelectedItem is not null;
}
