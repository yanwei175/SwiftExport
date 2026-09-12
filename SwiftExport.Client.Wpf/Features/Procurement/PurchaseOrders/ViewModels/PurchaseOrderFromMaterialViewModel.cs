using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

/// <summary>作为统一采购 Dialog 的薄容器，只负责页面切换、关闭门禁和最终结果交接。</summary>
public sealed partial class PurchaseOrderFromMaterialViewModel : ViewModelBaseEx, IDisposable
{
    private readonly PurchaseCandidateSelectionViewModel _preparation;
    private readonly PurchaseDraftWorkflowViewModel _preview;
    private readonly PurchaseDraftResultViewModel _result;
    private bool _disposed;

    /// <summary>初始化三个页面并连接页面级导航事件，不持有任何业务表格状态。</summary>
    public PurchaseOrderFromMaterialViewModel(
        PurchaseCandidateSelectionViewModel preparation,
        PurchaseDraftWorkflowViewModel preview)
    {
        _preparation = preparation;
        _preview = preview;
        _result = new PurchaseDraftResultViewModel(preview);
        _preparation.AttachWorkflow(preview);
        _preview.SetCandidateRecoveryHandler(RecoverCandidatesAsync);
        _preparation.PreviewRequested += OnPreviewRequested;
        _preview.ReturnToPreparationRequested += OnReturnToPreparationRequested;
        _preview.PreviewRefreshCompleted += OnPreviewRefreshCompleted;
        _preview.CandidateRecoveryCompleted += OnCandidateRecoveryCompleted;
        _preview.CreationCompleted += OnCreationCompleted;
        _preview.PropertyChanged += OnPreviewPropertyChanged;
        _result.ReturnToPreviewRequested += OnReturnToPreviewRequested;
        CurrentPage = _preparation;
        Title = "从物料需求建立采购草稿";
    }

    /// <summary>当前由 ContentControl 呈现的页面 ViewModel。</summary>
    [ObservableProperty]
    public partial object CurrentPage { get; private set; }

    /// <summary>当前业务阶段，仅供 Dialog 状态与测试观察。</summary>
    [ObservableProperty]
    public partial PurchaseOrderFromMaterialStage Stage { get; private set; }

    /// <summary>本次是否已经产生成功或幂等已应用结果。</summary>
    public bool HasAppliedResults => _preview.HasAppliedResults;
    /// <summary>采购写命令是否正在执行。</summary>
    public bool IsWriting => _preview.IsCreateInProgress;
    /// <summary>写入中或结果未知时禁止关闭，保留冻结命令的安全重试入口。</summary>
    public bool CanClose => _preview.CanClose;

    /// <summary>开始独立采购会话并显示采购准备页。</summary>
    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _preview.BeginSession();
        ShowPreparation();
        await _preparation.OpenAsync(cancellationToken);
    }

    private void OnPreviewRequested(object? sender, EventArgs e)
    {
        if (!_preparation.HasValidatedSelection || !_preview.HasValidPreparation) return;
        _preparation.SetInputEnabled(false);
        Stage = PurchaseOrderFromMaterialStage.Preview;
        CurrentPage = _preview;
    }

    private void OnReturnToPreparationRequested(object? sender, EventArgs e) => ShowPreparation();

    /// <summary>在审核命令内部等待当前来源完整恢复；成功后才返回准备，失败则保留原审核画面。</summary>
    private async Task<bool> RecoverCandidatesAsync(
        PurchaseDraftCandidateRecoveryRequestedEventArgs request,
        CancellationToken cancellationToken)
    {
        _preparation.SetInputEnabled(false);
        return await _preparation.RestoreCurrentScopeAsync(
            request.CompletedSourceKeys,
            cancellationToken);
    }

    private void OnCandidateRecoveryCompleted(object? sender, EventArgs e) => ShowPreparation();

    private void OnPreviewRefreshCompleted(object? sender, EventArgs e)
    {
        _preparation.AcceptCurrentPreviewValidation();
        if (_preview.HasBlockers || _preview.HasUnconfirmedWarnings)
            ShowPreparation();
    }

    private void OnCreationCompleted(object? sender, EventArgs e)
    {
        var hasFailure = _preview.Results.Any(result => result.Outcome is not
            NetDemandCommandOutcomeEnum.Succeeded and not NetDemandCommandOutcomeEnum.AlreadyApplied);
        if (hasFailure)
        {
            ShowPreparation();
            return;
        }
        Stage = PurchaseOrderFromMaterialStage.Result;
        CurrentPage = _result;
    }

    private void OnReturnToPreviewRequested(object? sender, EventArgs e)
    {
        Stage = PurchaseOrderFromMaterialStage.Preview;
        CurrentPage = _preview;
    }

    private void ShowPreparation()
    {
        _preparation.SetInputEnabled(true);
        Stage = PurchaseOrderFromMaterialStage.Selection;
        CurrentPage = _preparation;
    }

    private void OnPreviewPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PurchaseDraftWorkflowViewModel.HasAppliedResults))
            OnPropertyChanged(nameof(HasAppliedResults));
        if (e.PropertyName == nameof(PurchaseDraftWorkflowViewModel.IsCreateInProgress))
        {
            OnPropertyChanged(nameof(IsWriting));
            OnPropertyChanged(nameof(CanClose));
        }
        if (e.PropertyName == nameof(PurchaseDraftWorkflowViewModel.HasUnknownCreateResult))
            OnPropertyChanged(nameof(CanClose));
    }

    /// <summary>关闭弹窗时取消页面查询并结束集中会话。</summary>
    public void Close()
    {
        _preparation.Close();
        _preview.Close();
    }

    /// <summary>解除页面导航订阅并释放当前采购会话。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _preparation.PreviewRequested -= OnPreviewRequested;
        _preview.ReturnToPreparationRequested -= OnReturnToPreparationRequested;
        _preview.SetCandidateRecoveryHandler(null);
        _preview.PreviewRefreshCompleted -= OnPreviewRefreshCompleted;
        _preview.CandidateRecoveryCompleted -= OnCandidateRecoveryCompleted;
        _preview.CreationCompleted -= OnCreationCompleted;
        _preview.PropertyChanged -= OnPreviewPropertyChanged;
        _result.ReturnToPreviewRequested -= OnReturnToPreviewRequested;
        Close();
    }
}
