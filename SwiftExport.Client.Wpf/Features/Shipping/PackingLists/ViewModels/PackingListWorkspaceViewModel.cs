using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.Shipping;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using System.Collections.ObjectModel;

namespace SwiftExportClient.Features.Shipping.PackingLists.ViewModels;

public sealed partial class PackingListWorkspaceViewModel : ViewModelBaseEx, IAsyncInitializable, IDisposable
{
    public const string RouteKey = "shipping-packing-lists";
    private readonly IPackingListWorkspaceApi _api;
    private readonly IApiCallRunner _runner;
    private readonly INavigationService _navigation;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _sourceCancellation;
    private bool _loaded;

    public PackingListWorkspaceViewModel(IPackingListWorkspaceApi api, IApiCallRunner runner, INavigationService navigation)
    {
        _api = api; _runner = runner; _navigation = navigation; Title = "PackingList 工作台";
        RegisterBusyAwareCommand(SearchCommand); RegisterBusyAwareCommand(LoadSourcesCommand); RegisterBusyAwareCommand(CreateDraftCommand); RegisterBusyAwareCommand(OpenDetailCommand);
    }

    public ObservableCollection<PackingListListItemDto> Items { get; } = [];
    public ObservableCollection<PackingListSourceDto> Sources { get; } = [];
    [ObservableProperty] public partial string? Keyword { get; set; }
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(OpenDetailCommand))] public partial PackingListListItemDto? SelectedItem { get; set; }
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(CreateDraftCommand))] public partial PackingListSourceDto? SelectedSource { get; set; }
    [ObservableProperty] public partial string? Summary { get; set; }

    public Task InitializeAsync() { if (_loaded) return Task.CompletedTask; _loaded = true; return SearchAsync(); }

    [RelayCommand]
    private async Task SearchAsync()
    {
        _searchCancellation?.Cancel(); _searchCancellation?.Dispose(); _searchCancellation = new CancellationTokenSource(); var token = _searchCancellation.Token;
        await RunBusyAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.SearchAsync(new PackingListSearchRequest { Keyword = Keyword, PageIndex = 1, PageSize = 100 }, token), "查询 PackingList 失败");
            if (token.IsCancellationRequested) return; Items.Clear(); if (result.Success && result.Data is not null) foreach (var item in result.Data.Items) Items.Add(item);
            Summary = result.Success && result.Data is not null ? $"共 {result.Data.TotalCount} 张 PackingList。" : result.Message;
        }, "正在查询 PackingList...", false);
    }

    [RelayCommand]
    private async Task LoadSourcesAsync()
    {
        _sourceCancellation?.Cancel(); _sourceCancellation?.Dispose(); _sourceCancellation = new CancellationTokenSource(); var token = _sourceCancellation.Token;
        await RunBusyAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.SearchSourcesAsync(new PackingListSourceSearchRequest { Keyword = Keyword, PageIndex = 1, PageSize = 100 }, token), "查询可发货来源失败");
            if (token.IsCancellationRequested) return; Sources.Clear(); if (result.Success && result.Data is not null) foreach (var item in result.Data.Items) Sources.Add(item);
            Summary = result.Success && result.Data is not null ? $"可发货来源 {result.Data.TotalCount} 条。" : result.Message;
        }, "正在加载来源...", false);
    }

    [RelayCommand(CanExecute = nameof(CanCreateDraft))]
    private async Task CreateDraftAsync()
    {
        var source = SelectedSource!;
        await RunWriteAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.CreateDraftAsync(new CreatePackingListDraftCommand
            {
                CommandId = Guid.NewGuid(), Header = new PackingListHeaderCommand { CustomerId = source.CustomerId, PackingDate = DateTime.Today, ShipmentDate = DateTime.Today },
                Lines = [new PackingListContributionCommand { NetDemandAllocationId = source.NetDemandAllocationId, ExpectedSourceVersion = source.SourceVersion, ExpectedRemainingQty = source.ShippableRemainingQty, Quantity = source.ShippableRemainingQty }]
            }), "创建 PackingList 草稿失败");
            if (result.Success && result.Data is not null && result.Data.PackingListId != Guid.Empty)
                await _navigation.OpenDocumentAsync(PackingListDetailViewModel.RouteKey, $"packing-list:{result.Data.PackingListId:N}", result.Data.PackingListId);
            Summary = result.Data?.Message ?? result.Message; await SearchAsync();
        }, "正在创建 PackingList 草稿...");
    }

    [RelayCommand(CanExecute = nameof(CanOpenDetail))]
    private Task OpenDetailAsync() => _navigation.OpenDocumentAsync(PackingListDetailViewModel.RouteKey, $"packing-list:{SelectedItem!.Uid:N}", SelectedItem.Uid);

    private bool CanCreateDraft() => IsNotBusy && SelectedSource is not null;
    private bool CanOpenDetail() => IsNotBusy && SelectedItem is { Uid: var uid } && uid != Guid.Empty;
    public void Dispose() { _searchCancellation?.Cancel(); _searchCancellation?.Dispose(); _sourceCancellation?.Cancel(); _sourceCancellation?.Dispose(); }
}
