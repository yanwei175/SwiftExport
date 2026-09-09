using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;
using SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.DemandPlanning.GrossDemand.ViewModels;

public sealed partial class GrossDemandTraceViewModel : ViewModelBaseEx, IDisposable
{
    private readonly INetDemandWorkspaceApi _api;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private readonly INavigationService _navigationService;
    private CancellationTokenSource? _loadCancellation;
    private bool _disposed;

    public GrossDemandTraceViewModel(
        INetDemandWorkspaceApi api,
        IApiCallRunner apiCallRunner,
        IMessageService messageService,
        INavigationService navigationService)
    {
        _api = api;
        _apiCallRunner = apiCallRunner;
        _messageService = messageService;
        _navigationService = navigationService;
        Title = "需求追溯";
    }

    public ObservableCollection<NetDemandTraceItemDto> NetDemands { get; } = [];
    public ObservableCollection<DemandExecutionTraceItemDto> ExecutionLinks { get; } = [];

    [ObservableProperty]
    public partial GrossDemandDetailDto? GrossDemand { get; private set; }

    [ObservableProperty]
    public partial NetDemandTraceItemDto? SelectedNetDemand { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; private set; } = "请选择毛需求查看追溯。";

    public async Task<bool> LoadAsync(Guid uid)
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        StatusText = "正在加载需求追溯...";

        try
        {
            var result = await _apiCallRunner.RunAsync(
                () => _api.GetSourceDemandTraceAsync(uid, cancellation.Token),
                cancellation.Token,
                "加载需求追溯失败");
            if (cancellation.IsCancellationRequested || !ReferenceEquals(_loadCancellation, cancellation))
            {
                return false;
            }

            if (!result.Success || result.Data is null)
            {
                StatusText = result.Message;
                return false;
            }

            GrossDemand = result.Data.GrossDemand;
            NetDemands.Clear();
            foreach (var item in result.Data.NetDemands
                         .OrderByDescending(item => item.DecompositionRevision)
                         .ThenBy(item => item.LineNumber))
            {
                NetDemands.Add(item);
            }

            ExecutionLinks.Clear();
            foreach (var item in result.Data.ExecutionLinks)
            {
                ExecutionLinks.Add(item);
            }

            SelectedNetDemand = NetDemands.FirstOrDefault();
            StatusText = $"{NetDemands.Count} 条净需求，{ExecutionLinks.Count} 条执行追溯。";
            return true;
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation))
            {
                _loadCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    public void Clear()
    {
        _loadCancellation?.Cancel();
        GrossDemand = null;
        NetDemands.Clear();
        ExecutionLinks.Clear();
        SelectedNetDemand = null;
        StatusText = "请选择毛需求查看追溯。";
    }

    [RelayCommand]
    private async Task OpenNetDemandAsync(NetDemandTraceItemDto? netDemand)
    {
        if (netDemand is not null)
        {
            await _navigationService.OpenDocumentAsync(
                NetDemandWorkspaceViewModel.RouteKey,
                $"net-demand:{netDemand.Uid:N}",
                new NetDemandWorkspaceOpenRequest { NetDemandId = netDemand.Uid });
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }
}
