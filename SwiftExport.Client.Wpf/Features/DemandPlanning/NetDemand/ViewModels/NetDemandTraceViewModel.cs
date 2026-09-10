using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Services;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;

public sealed partial class NetDemandTraceViewModel : ViewModelBaseEx, IDisposable
{
    private readonly INetDemandWorkspaceApi _api;
    private readonly IPurchaseDraftApi _purchaseDraftApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly INetDemandTraceExportService _exportService;
    private readonly IMessageService _messageService;
    private CancellationTokenSource? _loadCancellation;
    private bool _disposed;

    public NetDemandTraceViewModel(
        INetDemandWorkspaceApi api,
        IPurchaseDraftApi purchaseDraftApi,
        IApiCallRunner apiCallRunner,
        INetDemandTraceExportService exportService,
        IMessageService messageService,
        IDialogService dialogService)
    {
        _api = api;
        _purchaseDraftApi = purchaseDraftApi;
        _apiCallRunner = apiCallRunner;
        _exportService = exportService;
        _messageService = messageService;
        _dialogService = dialogService;
        Title = "净需求追溯";
        RegisterBusyAwareCommand(ExportCommand);
        RegisterBusyAwareCommand(CancelPurchaseDraftCommand);
    }

    public ObservableCollection<NetDemandAllocationDto> AllocationHistory { get; } = [];
    public ObservableCollection<NetDemandExecutionLinkDto> ExecutionLinks { get; } = [];

    private readonly IDialogService _dialogService;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelPurchaseDraftCommand))]
    public partial NetDemandExecutionLinkDto? SelectedExecutionLink { get; set; }

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
            SelectedExecutionLink = null;
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
        SelectedExecutionLink = null;
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
            var traceResult = await _apiCallRunner.RunAsync(
                () => _purchaseDraftApi.GetSourceTraceAsync(summary.SourceType, summary.SourceUid),
                "加载来源采购追溯失败");
            if (!traceResult.Success || traceResult.Data is null)
            {
                _messageService.ShowError("无法导出来源采购追溯", traceResult.Message);
                return;
            }
            var path = await _exportService.ExportSourceAsync(traceResult.Data);
            if (path is null)
            {
                return;
            }

            StatusText = $"已导出 {AllocationHistory.Count} 个计划版本、{ExecutionLinks.Count} 条执行追溯。";
            _messageService.ShowSuccess("净需求追溯已导出", path);
        }, "正在导出净需求追溯...");
    }

    private bool CanExport() => IsNotBusy && Summary is not null;

    [CommunityToolkit.Mvvm.Input.RelayCommand(CanExecute = nameof(CanCancelPurchaseDraft))]
    private async Task CancelPurchaseDraftAsync()
    {
        var link = SelectedExecutionLink;
        var summary = Summary;
        if (link?.TargetHeadVersion is null || summary is null) return;
        if (!await _dialogService.ConfirmAsync(
                "取消采购草稿",
                $"确认取消采购草稿 {link.TargetNo}？全部活动 Link 将释放，净需求剩余量会恢复。",
                "确认取消",
                "返回"))
            return;

        await RunWriteAsync(async () =>
        {
            var result = await _apiCallRunner.RunAsync(
                () => _purchaseDraftApi.CancelAsync(link.TargetHeadId, new CancelPurchaseDraftCommand
                {
                    CommandId = Guid.NewGuid(),
                    ExpectedVersion = link.TargetHeadVersion.Value,
                    Reason = "净需求追溯人工取消",
                }),
                "取消采购草稿失败");
            if (!result.Success || result.Data is null)
            {
                _messageService.ShowError("取消采购草稿失败", result.Message);
                return;
            }
            _messageService.ShowSuccess("采购草稿已取消", result.Data.Message);
            await LoadAsync(summary.Uid);
        }, "正在取消采购草稿...");
    }

    private bool CanCancelPurchaseDraft() => IsNotBusy
        && SelectedExecutionLink is
        {
            TargetType: "PO",
            LinkStatus: DemandExecutionLinkStatusEnum.Active,
            TargetHeadVersion: > 0,
            TargetHeadLifecycleStatus: PurchaseOrderLifecycleStatusEnum.Active,
            TargetHeadConfirmationStatus: ConfirmationStatusEnum.待确认,
        };

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
