using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using System.Collections.ObjectModel;

namespace SwiftExportClient.Features.Inventory.Outbounds.ViewModels;

public sealed partial class OutboundWorkspaceViewModel : ViewModelBaseEx, IAsyncInitializable
{
    public const string RouteKey = "inventory-outbounds";
    private readonly IOutboundWorkspaceApi _api;
    private readonly IApiCallRunner _runner;
    private CancellationTokenSource? _searchCancellation;
    private bool _loaded;

    public OutboundWorkspaceViewModel(IOutboundWorkspaceApi api, IApiCallRunner runner)
    {
        _api = api; _runner = runner; Title = "出库工作台"; IsReadOnly = true;
        RegisterBusyAwareCommand(SearchCommand); RegisterBusyAwareCommand(LoadDetailCommand);
    }

    public ObservableCollection<OutboundListItemDto> Items { get; } = [];
    public ObservableCollection<OutboundLineDto> Lines { get; } = [];
    [ObservableProperty] public partial string? Keyword { get; set; }
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(LoadDetailCommand))] public partial OutboundListItemDto? SelectedItem { get; set; }
    [ObservableProperty] public partial string? Summary { get; set; }

    public Task InitializeAsync() { if (_loaded) return Task.CompletedTask; _loaded = true; return SearchAsync(); }

    [RelayCommand]
    private async Task SearchAsync()
    {
        _searchCancellation?.Cancel(); _searchCancellation?.Dispose(); _searchCancellation = new CancellationTokenSource();
        var token = _searchCancellation.Token;
        await RunBusyAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.SearchAsync(new OutboundSearchRequest { Keyword = Keyword, PageIndex = 1, PageSize = 100 }, token), "查询出库单失败");
            if (token.IsCancellationRequested) return;
            Items.Clear(); if (result.Success && result.Data is not null) foreach (var item in result.Data.Items) Items.Add(item);
            Summary = result.Success && result.Data is not null ? $"共 {result.Data.TotalCount} 张正式出库单。" : result.Message;
        }, "正在查询出库单...", false);
    }

    [RelayCommand(CanExecute = nameof(CanLoadDetail))]
    private async Task LoadDetailAsync()
    {
        var result = await _runner.RunAsync(() => _api.GetDetailAsync(SelectedItem!.Uid), "加载出库明细失败");
        Lines.Clear(); if (result.Success && result.Data is not null) foreach (var line in result.Data.Lines) Lines.Add(line);
        Summary = result.Message;
    }

    private bool CanLoadDetail() => IsNotBusy && SelectedItem is { Uid: var uid } && uid != Guid.Empty;
}
