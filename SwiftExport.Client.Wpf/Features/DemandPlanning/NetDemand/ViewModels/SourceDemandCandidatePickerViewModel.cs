using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;

/// <summary>负责候选来源需求的独立查询、选择及过期请求隔离。</summary>
public sealed partial class SourceDemandCandidatePickerViewModel(
    INetDemandWorkspaceApi api,
    IApiCallRunner apiCallRunner) : ObservableObject, IDisposable
{
    private CancellationTokenSource? _searchCancellation;
    private bool _disposed;

    public ObservableCollection<SourceDemandCandidateRowModel> Items { get; } = [];
    public IReadOnlyList<SourceDemandCandidateDto> SelectedRows => Items.Where(item => item.IsChecked).Select(item => item.Demand).ToArray();

    [ObservableProperty]
    public partial string Keyword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string StatusText { get; private set; } = "正在加载候选...";

    /// <summary>打开选择器时重新加载第一页可建立来源需求。</summary>
    public Task OpenAsync(CancellationToken cancellationToken = default) => SearchCoreAsync(cancellationToken);

    [RelayCommand]
    private Task SearchAsync() => SearchCoreAsync();

    private async Task SearchCoreAsync(CancellationToken externalCancellation = default)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(externalCancellation);
        _searchCancellation = cancellation;
        IsBusy = true;
        try
        {
            var result = await apiCallRunner.RunAsync(
                () => api.SearchSourceDemandCandidatesAsync(new SourceDemandCandidateSearchRequest
                {
                    Keyword = Keyword.Trim(), PageIndex = 1, PageSize = 200
                }, cancellation.Token),
                cancellation.Token,
                "查询来源需求候选失败");
            if (_disposed || cancellation.IsCancellationRequested || !ReferenceEquals(_searchCancellation, cancellation)) return;
            Items.Clear();
            if (!result.Success || result.Data is null)
            {
                StatusText = result.Message;
                return;
            }
            foreach (var item in result.Data.Items.Where(item => item.CanEstablish))
                Items.Add(new SourceDemandCandidateRowModel { Demand = item });
            StatusText = $"共 {result.Data.TotalCount} 条可建立来源需求。";
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, cancellation))
            {
                _searchCancellation = null;
                IsBusy = false;
            }
            cancellation.Dispose();
        }
    }

    /// <summary>终止在途查询，防止对话框关闭后回写集合。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = null;
    }
}
