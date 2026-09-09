using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;

/// <summary>负责按来源单加载完整待建立需求、维护选择状态并隔离过期请求。</summary>
public sealed partial class SourceDemandCandidatePickerViewModel(
    INetDemandWorkspaceApi api,
    IApiCallRunner apiCallRunner) : ObservableObject, IDisposable
{
    private CancellationTokenSource? _groupCancellation;
    private CancellationTokenSource? _detailCancellation;
    private bool _disposed;

    /// <summary>全部仍包含待建立行的来源单，不从分页候选结果推导。</summary>
    public ObservableCollection<SourceDemandCandidateGroupModel> SourceGroups { get; } = [];

    /// <summary>当前来源单一次完整加载的全部待建立行。</summary>
    public ObservableCollection<SourceDemandCandidateRowModel> Items { get; } = [];

    /// <summary>返回当前来源单内用户已勾选的来源需求。</summary>
    public IReadOnlyList<SourceDemandCandidateDto> SelectedRows =>
        Items.Where(item => item.IsChecked).Select(item => item.Demand).ToArray();

    /// <summary>返回当前来源单是否已全部勾选。</summary>
    public bool IsAllChecked => Items.Count > 0 && Items.All(item => item.IsChecked);

    /// <summary>只有当前来源的整单明细成功加载且存在勾选项时才允许提交。</summary>
    public bool CanSubmit => IsDetailLoaded && !IsBusy && !IsWriting && SelectedRows.Count > 0;

    [ObservableProperty]
    public partial SourceDemandCandidateGroupModel? SelectedSourceGroup { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    public partial bool IsDetailLoaded { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    public partial bool IsWriting { get; private set; }

    [ObservableProperty]
    public partial bool HasEstablished { get; private set; }

    [ObservableProperty]
    public partial string SourceGroupStatusText { get; private set; } = "正在加载来源单...";

    [ObservableProperty]
    public partial string StatusText { get; private set; } = "请选择来源单。";

    /// <summary>打开选择器时重新取得完整来源单列表，并加载第一张来源单的全部候选。</summary>
    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        CancelPendingRequests();
        ClearItems();
        SourceGroups.Clear();
        SelectedSourceGroup = null;
        IsDetailLoaded = false;
        HasEstablished = false;
        SourceGroupStatusText = "正在加载来源单...";
        StatusText = "请选择来源单。";

        var first = await LoadGroupsAsync(cancellationToken);
        if (first is not null)
        {
            await SelectSourceGroupCoreAsync(first, cancellationToken);
        }
    }

    /// <summary>切换来源单时取消上一单查询，并只接纳当前请求的完整结果。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SelectSourceGroupAsync(SourceDemandCandidateGroupModel? group)
        => group is null || IsWriting || (ReferenceEquals(SelectedSourceGroup, group)
            && (IsDetailLoaded || _detailCancellation is not null))
            ? Task.CompletedTask
            : SelectSourceGroupCoreAsync(group);

    /// <summary>切换当前来源单全部候选的勾选状态。</summary>
    [RelayCommand]
    private void ToggleAll()
    {
        if (!IsDetailLoaded || IsBusy || IsWriting || Items.Count == 0)
        {
            return;
        }

        var target = !IsAllChecked;
        foreach (var item in Items)
        {
            item.IsChecked = target;
        }
    }

    /// <summary>提交当前勾选行，并在成功后刷新来源分组和仍未建立的当前明细。</summary>
    [RelayCommand(CanExecute = nameof(CanEstablish))]
    private async Task EstablishSelectedAsync()
    {
        if (!CanEstablish())
        {
            return;
        }

        var currentGroup = SelectedSourceGroup!;
        var selected = SelectedRows;
        IsWriting = true;
        try
        {
            var result = await apiCallRunner.RunAsync(
                () => api.EstablishFromSourcesAsync(new EstablishMaterialDemandsCommand
                {
                    CommandId = Guid.NewGuid(),
                    Items = selected.Select(item => new EstablishMaterialDemandItem
                    {
                        GrossDemandId = item.GrossDemandId,
                        SourceDemandVersion = item.SourceDemandVersion,
                        SourceVersion = item.SourceVersion,
                    }).ToList(),
                }),
                "从来源需求建立物料需求失败");
            if (!result.Success || result.Data is null)
            {
                StatusText = result.Message;
                return;
            }

            HasEstablished = true;
            SourceGroups.Clear();
            SelectedSourceGroup = null;
            IsDetailLoaded = false;
            ClearItems();
            var nextGroup = await LoadGroupsAsync(CancellationToken.None, currentGroup);
            if (nextGroup is not null)
            {
                await SelectSourceGroupCoreAsync(nextGroup);
            }

            StatusText = $"建立完成：新增来源 {result.Data.CreatedSourceCount} 条、物料需求 {result.Data.CreatedMaterialDemandCount} 条；"
                + $"已存在 {result.Data.AlreadyEstablishedCount} 条。{StatusText}";
        }
        finally
        {
            IsWriting = false;
        }
    }

    private bool CanEstablish() => CanSubmit;

    /// <summary>加载不分页的来源单分组；失败时不把错误伪装成空结果。</summary>
    private async Task<SourceDemandCandidateGroupModel?> LoadGroupsAsync(
        CancellationToken externalCancellation,
        SourceDemandCandidateGroupModel? preferredGroup = null)
    {
        var cancellation = ReplaceCancellation(ref _groupCancellation, externalCancellation);
        IsBusy = true;
        try
        {
            var result = await apiCallRunner.RunAsync(
                () => api.GetSourceDemandCandidateGroupsAsync(cancellation.Token),
                cancellation.Token,
                "查询待建立来源单失败");
            if (!CanAccept(_groupCancellation, cancellation))
            {
                return null;
            }

            if (!result.Success || result.Data is null)
            {
                SourceGroupStatusText = result.Message;
                StatusText = "来源单加载失败，无法提交。";
                return null;
            }

            foreach (var group in result.Data)
            {
                SourceGroups.Add(SourceDemandCandidateGroupModel.FromDto(group));
            }

            SourceGroupStatusText = SourceGroups.Count == 0
                ? "没有待建立物料需求的来源单。"
                : $"共 {SourceGroups.Count} 张来源单。";
            return preferredGroup is null
                ? SourceGroups.FirstOrDefault()
                : SourceGroups.FirstOrDefault(group => group.SourceUid == preferredGroup.SourceUid
                    && string.Equals(group.SourceType, preferredGroup.SourceType, StringComparison.Ordinal))
                  ?? SourceGroups.FirstOrDefault();
        }
        finally
        {
            CompleteRequest(ref _groupCancellation, cancellation);
        }
    }

    /// <summary>按来源类型和 Uid 完整加载一张来源单，过期或失败结果不得覆盖当前选择。</summary>
    private async Task SelectSourceGroupCoreAsync(
        SourceDemandCandidateGroupModel group,
        CancellationToken externalCancellation = default)
    {
        ThrowIfDisposed();
        var cancellation = ReplaceCancellation(ref _detailCancellation, externalCancellation);
        IsDetailLoaded = false;
        ClearItems();
        StatusText = $"正在加载 {group.DisplaySourceNo}...";
        IsBusy = true;
        try
        {
            // 属性通知会同步触发列表选择事件；先准备请求状态，再发布选择并检查是否被替代。
            SelectedSourceGroup = group;
            if (!CanAccept(_detailCancellation, cancellation)) return;
            var result = await apiCallRunner.RunAsync(
                () => api.GetSourceDemandCandidatesBySourceAsync(
                    group.SourceType, group.SourceUid, cancellation.Token),
                cancellation.Token,
                "查询来源单待建立明细失败");
            if (!CanAccept(_detailCancellation, cancellation)
                || !ReferenceEquals(SelectedSourceGroup, group))
            {
                return;
            }

            if (!result.Success || result.Data is null)
            {
                StatusText = result.Message;
                return;
            }

            foreach (var demand in result.Data.Where(item => item.CanEstablish))
            {
                var row = new SourceDemandCandidateRowModel { Demand = demand };
                row.PropertyChanged += OnRowPropertyChanged;
                Items.Add(row);
            }

            IsDetailLoaded = true;
            StatusText = Items.Count == 0
                ? "该来源单已无待建立需求，请刷新后重新选择。"
                : $"已完整加载 {Items.Count} 条待建立需求。";
            OnPropertyChanged(nameof(IsAllChecked));
            OnPropertyChanged(nameof(CanSubmit));
        }
        finally
        {
            CompleteRequest(ref _detailCancellation, cancellation);
        }
    }

    /// <summary>把行勾选变化提升为页面级全选与提交状态通知。</summary>
    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SourceDemandCandidateRowModel.IsChecked))
        {
            return;
        }

        OnPropertyChanged(nameof(IsAllChecked));
        OnPropertyChanged(nameof(CanSubmit));
        EstablishSelectedCommand.NotifyCanExecuteChanged();
    }

    /// <summary>清空当前来源行并解除事件订阅，避免跨来源保留勾选状态。</summary>
    private void ClearItems()
    {
        foreach (var item in Items)
        {
            item.PropertyChanged -= OnRowPropertyChanged;
        }
        Items.Clear();
        OnPropertyChanged(nameof(IsAllChecked));
        OnPropertyChanged(nameof(CanSubmit));
        EstablishSelectedCommand.NotifyCanExecuteChanged();
    }

    /// <summary>先交接请求所有权再取消旧查询；取消源由所属请求的 finally 释放。</summary>
    private static CancellationTokenSource ReplaceCancellation(
        ref CancellationTokenSource? target,
        CancellationToken externalCancellation)
    {
        var previous = target;
        target = CancellationTokenSource.CreateLinkedTokenSource(externalCancellation);
        var current = target;
        previous?.Cancel();
        return current;
    }

    /// <summary>确认请求仍属于当前页面实例及当前查询入口。</summary>
    private bool CanAccept(CancellationTokenSource? owner, CancellationTokenSource request)
        => !_disposed && !request.IsCancellationRequested && ReferenceEquals(owner, request);

    /// <summary>仅允许当前请求结束自己拥有的 Busy 与取消源。</summary>
    private void CompleteRequest(ref CancellationTokenSource? owner, CancellationTokenSource request)
    {
        if (ReferenceEquals(owner, request))
        {
            owner = null;
            IsBusy = false;
        }
        request.Dispose();
    }

    /// <summary>先移除请求所有权再取消，防止取消回调重入；在途请求自行释放资源。</summary>
    private void CancelPendingRequests()
    {
        var groups = _groupCancellation;
        var details = _detailCancellation;
        _groupCancellation = null;
        _detailCancellation = null;
        groups?.Cancel();
        details?.Cancel();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>关闭当前对话框时取消查询，但保留实例供下一次打开重新初始化。</summary>
    public void Close()
    {
        CancelPendingRequests();
        IsBusy = false;
    }

    partial void OnIsBusyChanged(bool value) => EstablishSelectedCommand.NotifyCanExecuteChanged();

    partial void OnIsDetailLoadedChanged(bool value) => EstablishSelectedCommand.NotifyCanExecuteChanged();

    partial void OnIsWritingChanged(bool value) => EstablishSelectedCommand.NotifyCanExecuteChanged();

    /// <summary>取消全部在途查询并解除行事件，防止对话框宿主释放后继续回写。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Close();
        ClearItems();
    }
}
