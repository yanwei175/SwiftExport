using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Inventory.Balances.ViewModels;

public sealed partial class InventoryBalancesViewModel : ViewModelBaseEx, IAsyncInitializable
{
    public const string RouteKey = "inventory-balances";
    private readonly IInventoryApi _api; private readonly IApiCallRunner _runner; private readonly IMessageService _messages; private bool _loaded;
    public InventoryBalancesViewModel(IInventoryApi api, IApiCallRunner runner, IMessageService messages) { _api = api; _runner = runner; _messages = messages; Title = "库存余额"; RegisterBusyAwareCommand(SearchCommand); RegisterBusyAwareCommand(PreviewOpeningCommand); RegisterBusyAwareCommand(PostOpeningCommand); }
    public ObservableCollection<InventoryBalanceDto> Items { get; } = [];
    public ObservableCollection<InventoryLedgerItemDto> TraceItems { get; } = [];
    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty] public partial bool NonZeroOnly { get; set; } = true;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(LoadTraceCommand))] public partial InventoryBalanceDto? SelectedItem { get; set; }
    [ObservableProperty] public partial string OpeningWarehouseId { get; set; } = string.Empty;
    [ObservableProperty] public partial string OpeningProductId { get; set; } = string.Empty;
    [ObservableProperty] public partial decimal OpeningQuantity { get; set; }
    [ObservableProperty] public partial string OpeningReason { get; set; } = string.Empty;
    [ObservableProperty] public partial string ResultSummary { get; set; } = "尚未加载。";
    public Task InitializeAsync() { if (_loaded) return Task.CompletedTask; _loaded = true; return LoadAsync(); }
    [RelayCommand] private Task SearchAsync() => LoadAsync();
    [RelayCommand(CanExecute = nameof(CanLoadTrace))] private async Task LoadTraceAsync()
    { var item = SelectedItem; if (item is null) return; var result = await _runner.RunAsync(() => _api.GetTraceAsync(item.Uid), "加载库存追溯失败"); TraceItems.Clear(); if (!result.Success || result.Data is null) { _messages.ShowError("追溯失败", result.Message); return; } foreach (var row in result.Data.Ledgers) TraceItems.Add(row); }
    [RelayCommand] private async Task PreviewOpeningAsync() { var command = CreateOpeningCommand(); if (command is null) return; var result = await _runner.RunAsync(() => _api.PreviewOpeningBalancesAsync(command), "期初预览失败"); if (!result.Success) _messages.ShowError("预览失败", result.Message); else _messages.ShowSuccess("期初预览完成", $"{result.Data?.Count ?? 0} 条明细可过账。"); }
    [RelayCommand] private async Task PostOpeningAsync() { var command = CreateOpeningCommand(); if (command is null) return; await RunWriteAsync(async () => { var result = await _runner.RunAsync(() => _api.PostOpeningBalancesAsync(command), "期初过账失败"); if (!result.Success) { _messages.ShowError("过账失败", result.Message); return; } _messages.ShowSuccess("期初已过账", $"{result.Data?.Count ?? 0} 条库存流水已生成。"); await LoadAsync(); }, "正在过账期初余额..."); }
    private OpeningBalanceCommand? CreateOpeningCommand() { if (!Guid.TryParse(OpeningWarehouseId, out var warehouseId) || !Guid.TryParse(OpeningProductId, out var productId) || OpeningQuantity == 0 || string.IsNullOrWhiteSpace(OpeningReason)) { _messages.ShowWarning("期初资料不完整", "请输入仓库 Uid、产品 Uid、非零数量和原因。 "); return null; } return new OpeningBalanceCommand { CommandId = Guid.NewGuid(), Lines = [new OpeningBalanceLineCommand { CommandItemId = Guid.NewGuid(), WarehouseSiteId = warehouseId, ProductId = productId, QuantityDelta = OpeningQuantity, Reason = OpeningReason }] }; }
    private bool CanLoadTrace() => IsNotBusy && SelectedItem is not null;
    private async Task LoadAsync() => await RunBusyAsync(async () => { var result = await _runner.RunAsync(() => _api.SearchBalancesAsync(new InventoryBalanceSearchRequest { Keyword = Keyword, NonZeroOnly = NonZeroOnly, PageIndex = 1, PageSize = 200 }), "查询库存余额失败"); Items.Clear(); if (result.Success && result.Data is not null) foreach (var row in result.Data.Items) Items.Add(row); ResultSummary = result.Success && result.Data is not null ? $"共 {result.Data.TotalCount} 条余额。" : result.Message; }, "正在查询库存余额...", false);
}
