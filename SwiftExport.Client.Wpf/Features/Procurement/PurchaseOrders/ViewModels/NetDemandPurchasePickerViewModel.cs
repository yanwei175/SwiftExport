using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

public sealed partial class NetDemandPurchasePickerViewModel(INetDemandWorkspaceApi api, IApiCallRunner runner) : ViewModelBaseEx
{
    private CancellationTokenSource? _searchCancellation;
    public ObservableCollection<NetDemandRowModel> Items { get; } = [];
    public IReadOnlyList<NetDemandRowModel> SelectedRows => Items.Where(x => x.IsChecked && x.RemainingQty > 0).ToList();
    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty] public partial string SourceNo { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusText { get; set; } = "请选择待转采购净需求。";

    public Task OpenAsync(CancellationToken cancellationToken = default) => LoadAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SearchAsync() => LoadAsync();

    private async Task LoadAsync(CancellationToken external = default)
    {
        _searchCancellation?.Cancel(); _searchCancellation?.Dispose();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(external); _searchCancellation = cts;
        try
        {
            var result = await runner.RunAsync(() => api.SearchAsync(new NetDemandSearchRequest
            {
                Keyword = Keyword.Trim(), SourceNos = string.IsNullOrWhiteSpace(SourceNo) ? new List<string>() : new List<string> { SourceNo.Trim() },
                HasRemainingQty = true, PageIndex = 1, PageSize = 200
            }, cts.Token), cts.Token, "加载待采购净需求失败");
            if (cts.IsCancellationRequested || !ReferenceEquals(_searchCancellation, cts)) return;
            Items.Clear();
            if (result.Success && result.Data is not null)
                foreach (var dto in result.Data.Items.Where(x => x.CanTransferPurchase && x.RemainingQty > 0)) Items.Add(NetDemandRowModel.FromDto(dto));
            StatusText = result.Success ? $"当前页可选择 {Items.Count} 条；将按全部剩余量转采购。" : result.Message;
        }
        finally { if (ReferenceEquals(_searchCancellation, cts)) _searchCancellation = null; cts.Dispose(); }
    }
}
