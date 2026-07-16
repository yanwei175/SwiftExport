using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Services;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;

public sealed partial class NetDemandTraceViewModel : ViewModelBaseEx, IDisposable
{
    private readonly INetDemandWorkspaceApi _api;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly INetDemandTraceExportService _exportService;
    private readonly IMessageService _messageService;
    private CancellationTokenSource? _loadCancellation;
    private bool _disposed;

    public NetDemandTraceViewModel(
        INetDemandWorkspaceApi api,
        IApiCallRunner apiCallRunner,
        INetDemandTraceExportService exportService,
        IMessageService messageService)
    {
        _api = api;
        _apiCallRunner = apiCallRunner;
        _exportService = exportService;
        _messageService = messageService;
        Title = "净需求追溯";
        RegisterBusyAwareCommand(ExportCommand);
    }

    public ObservableCollection<NetDemandAllocationDto> AllocationHistory { get; } = [];
    public ObservableCollection<NetDemandExecutionLinkDto> ExecutionLinks { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial NetDemandListItemDto? Summary { get; private set; }

    [ObservableProperty]
    public partial string StatusText { get; private set; } = "请选择净需求查看追溯。";

    public async Task<bool> LoadAsync(Guid uid)
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        StatusText = "正在加载净需求追溯...";

        try
        {
            var result = await _apiCallRunner.RunAsync(
                () => _api.GetTraceAsync(uid, cancellation.Token),
                cancellation.Token,
                "加载净需求追溯失败");
            if (cancellation.IsCancellationRequested || !ReferenceEquals(_loadCancellation, cancellation))
            {
                return false;
            }

            if (!result.Success || result.Data is null)
            {
                StatusText = result.Message;
                return false;
            }

            Summary = result.Data.Summary;
            AllocationHistory.Clear();
            foreach (var allocation in result.Data.AllocationHistory
                         .OrderByDescending(item => item.PlanRevision)
                         .ThenBy(item => item.Sequence))
            {
                AllocationHistory.Add(allocation);
            }

            ExecutionLinks.Clear();
            foreach (var link in result.Data.ExecutionLinks)
            {
                ExecutionLinks.Add(link);
            }

            StatusText = $"{AllocationHistory.Count} 个计划版本，{ExecutionLinks.Count} 条执行追溯。";
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
        Summary = null;
        AllocationHistory.Clear();
        ExecutionLinks.Clear();
        StatusText = "请选择净需求查看追溯。";
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        var summary = Summary;
        if (summary is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var path = await _exportService.ExportAsync(
                summary,
                AllocationHistory.ToArray(),
                ExecutionLinks.ToArray());
            if (path is null)
            {
                return;
            }

            StatusText = $"已导出 {AllocationHistory.Count} 个计划版本、{ExecutionLinks.Count} 条执行追溯。";
            _messageService.ShowSuccess("净需求追溯已导出", path);
        }, "正在导出净需求追溯...");
    }

    private bool CanExport() => IsNotBusy && Summary is not null;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
    }
}
