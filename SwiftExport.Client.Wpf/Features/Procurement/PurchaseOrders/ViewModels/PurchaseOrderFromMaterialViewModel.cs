using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
/// <summary>统一采购弹窗只在准备和创建结果之间切换，未决提交期间保留会话。</summary>
public sealed partial class PurchaseOrderFromMaterialViewModel : ViewModelBaseEx, IDisposable
{
    private readonly PurchaseCandidateSelectionViewModel _preparation;
    private readonly PurchaseDraftWorkflowViewModel _session;
    private readonly PurchaseDraftResultViewModel _result;
    private bool _disposed;
    /// <summary>连接输入、直接创建及结果，不构造独立审核页面。</summary>
    public PurchaseOrderFromMaterialViewModel(PurchaseCandidateSelectionViewModel preparation, PurchaseDraftWorkflowViewModel workflow,
        INavigationService? navigation = null, ICurrentUserService? currentUser = null)
    {
        _preparation = preparation; _session = workflow; _result = new(workflow, navigation, currentUser);
        preparation.AttachWorkflow(workflow); workflow.CreationCompleted += OnCreated; workflow.PropertyChanged += OnSessionChanged;
        _result.ReturnToPreparationRequested += OnReturn; CurrentPage = preparation; Title = "从物料需求建立采购草稿";
    }
    [ObservableProperty] public partial object CurrentPage { get; private set; }
    [ObservableProperty] public partial PurchaseOrderFromMaterialStage Stage { get; private set; }
    public bool HasAppliedResults => _session.HasAppliedResults;
    public bool IsWriting => _session.IsCreateInProgress;
    public bool CanClose => _session.CanClose;
    /// <summary>开启新会话，保留当前准备页完整的候选加载边界。</summary>
    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _session.OpenSession();
        ShowPreparation();
        if (_session.HasUnknownCreateResult) _preparation.RestorePendingSelection(_session.RecoveryRows);
        else await _preparation.OpenAsync(cancellationToken);
    }
    private void OnCreated(object? sender, EventArgs e) { Stage = PurchaseOrderFromMaterialStage.Result; CurrentPage = _result; }
    private void OnReturn(object? sender, EventArgs e) => ShowPreparation();
    private void ShowPreparation() { _preparation.SetInputEnabled(_session.CanEditInputs); Stage = PurchaseOrderFromMaterialStage.Selection; CurrentPage = _preparation; }
    private void OnSessionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasAppliedResults)); OnPropertyChanged(nameof(IsWriting)); OnPropertyChanged(nameof(CanClose));
        if (e.PropertyName == nameof(PurchaseDraftWorkflowViewModel.HasUnknownCreateResult) && _session.HasUnknownCreateResult
            || e.PropertyName == nameof(PurchaseDraftWorkflowViewModel.StatusText) && !_session.CanEditInputs && _session.RecoveryRows.Count == 0)
            _preparation.RestorePendingSelection(_session.RecoveryRows);
    }
    /// <summary>关闭期间有未决提交时保留全部输入与原请求。</summary>
    public void Close() { if (!CanClose) return; _preparation.Close(); _session.Close(); }
    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed || !CanClose) return; _disposed = true;
        _session.CreationCompleted -= OnCreated; _session.PropertyChanged -= OnSessionChanged; _result.ReturnToPreparationRequested -= OnReturn; Close();
    }
}
