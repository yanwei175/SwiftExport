using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Inventory.WarehouseSites.ViewModels;

public sealed partial class WarehouseSitesViewModel : ViewModelBaseEx, IAsyncInitializable
{
    public const string RouteKey = "inventory-warehouse-sites";
    private readonly IWarehouseSiteApi _api; private readonly IApiCallRunner _runner; private readonly IMessageService _messages; private bool _loaded;
    public WarehouseSitesViewModel(IWarehouseSiteApi api, IApiCallRunner runner, IMessageService messages) { _api = api; _runner = runner; _messages = messages; Title = "供应商虚拟仓"; RegisterBusyAwareCommand(SearchCommand); RegisterBusyAwareCommand(CreateCommand); RegisterBusyAwareCommand(DeactivateCommand); }
    public ObservableCollection<WarehouseSiteDto> Items { get; } = [];
    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty] public partial string SupplierIdText { get; set; } = string.Empty;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(DeactivateCommand))] public partial WarehouseSiteDto? SelectedItem { get; set; }
    [ObservableProperty] public partial string NewCode { get; set; } = string.Empty;
    [ObservableProperty] public partial string NewName { get; set; } = string.Empty;
    [ObservableProperty] public partial string NewSupplierId { get; set; } = string.Empty;
    [ObservableProperty] public partial string ResultSummary { get; set; } = "尚未加载。";
    public Task InitializeAsync() { if (_loaded) return Task.CompletedTask; _loaded = true; return LoadAsync(); }
    [RelayCommand] private Task SearchAsync() => LoadAsync();
    [RelayCommand] private async Task CreateAsync()
    {
        if (!Guid.TryParse(NewSupplierId, out var supplierId) || string.IsNullOrWhiteSpace(NewCode) || string.IsNullOrWhiteSpace(NewName)) { _messages.ShowWarning("资料不完整", "请输入供应商 Uid、仓库代码和名称。供应商必须精确选择。 "); return; }
        await RunWriteAsync(async () => { var result = await _runner.RunAsync(() => _api.CreateSupplierVirtualAsync(new CreateSupplierVirtualWarehouseCommand { CommandId = Guid.NewGuid(), SupplierId = supplierId, Code = NewCode, Name = NewName }), "创建虚拟仓失败"); if (!result.Success) { _messages.ShowError("创建失败", result.Message); return; } NewCode = NewName = NewSupplierId = string.Empty; _messages.ShowSuccess("已创建", result.Data?.Code ?? string.Empty); await LoadAsync(); }, "正在创建虚拟仓...");
    }
    [RelayCommand(CanExecute = nameof(CanDeactivate))] private async Task DeactivateAsync()
    {
        var item = SelectedItem; if (item is null) return;
        await RunWriteAsync(async () => { var result = await _runner.RunAsync(() => _api.DeactivateAsync(item.Uid, new DeactivateWarehouseSiteCommand { CommandId = Guid.NewGuid(), ExpectedVersion = item.Version, Reason = "人工停用" }), "停用虚拟仓失败"); if (!result.Success) { _messages.ShowError("停用失败", result.Message); return; } await LoadAsync(); }, "正在停用虚拟仓...");
    }
    private bool CanDeactivate() => IsNotBusy && SelectedItem?.IsActive == true;
    private async Task LoadAsync() => await RunBusyAsync(async () => { Guid? supplierId = Guid.TryParse(SupplierIdText, out var value) ? value : null; var result = await _runner.RunAsync(() => _api.SearchAsync(new WarehouseSiteSearchRequest { Keyword = Keyword, SupplierId = supplierId, PageIndex = 1, PageSize = 200 }), "查询虚拟仓失败"); Items.Clear(); if (result.Success && result.Data is not null) foreach (var row in result.Data.Items) Items.Add(row); ResultSummary = result.Success && result.Data is not null ? $"共 {result.Data.TotalCount} 个虚拟仓。" : result.Message; }, "正在查询虚拟仓...", false);
}
