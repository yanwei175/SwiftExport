using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.Inventory.Ledger.ViewModels;

public sealed partial class InventoryLedgerViewModel : ViewModelBaseEx, IAsyncInitializable
{
    public const string RouteKey = "inventory-ledger";
    private readonly IInventoryApi _api; private readonly IApiCallRunner _runner; private bool _loaded;
    public InventoryLedgerViewModel(IInventoryApi api, IApiCallRunner runner) { _api = api; _runner = runner; Title = "库存流水"; RegisterBusyAwareCommand(SearchCommand); }
    public ObservableCollection<InventoryLedgerItemDto> Items { get; } = [];
    public IReadOnlyList<InventoryTransactionTypeEnum?> TransactionTypes { get; } = [null, InventoryTransactionTypeEnum.PurchaseReceiptInbound, InventoryTransactionTypeEnum.CustomerSuppliedReceiptInbound, InventoryTransactionTypeEnum.PackingListAutoReceiptInbound, InventoryTransactionTypeEnum.FinalCustomerShipmentOutbound, InventoryTransactionTypeEnum.CustomerSuppliedConsumptionOutbound, InventoryTransactionTypeEnum.NegativeStockExceptionOutbound, InventoryTransactionTypeEnum.OpeningBalance, InventoryTransactionTypeEnum.Reversal];
    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty] public partial InventoryTransactionTypeEnum? TransactionType { get; set; }
    [ObservableProperty] public partial DateTime? DateFrom { get; set; } = DateTime.Today.AddMonths(-1);
    [ObservableProperty] public partial DateTime? DateTo { get; set; } = DateTime.Today;
    [ObservableProperty] public partial string ResultSummary { get; set; } = "尚未加载。";
    public Task InitializeAsync() { if (_loaded) return Task.CompletedTask; _loaded = true; return LoadAsync(); }
    [RelayCommand] private Task SearchAsync() => LoadAsync();
    private async Task LoadAsync() => await RunBusyAsync(async () => { var result = await _runner.RunAsync(() => _api.SearchLedgerAsync(new InventoryLedgerSearchRequest { Keyword = Keyword, TransactionType = TransactionType, OccurredDateFrom = DateFrom, OccurredDateTo = DateTo, PageIndex = 1, PageSize = 200 }), "查询库存流水失败"); Items.Clear(); if (result.Success && result.Data is not null) foreach (var row in result.Data.Items) Items.Add(row); ResultSummary = result.Success && result.Data is not null ? $"共 {result.Data.TotalCount} 条流水。" : result.Message; }, "正在查询库存流水...", false);
}
