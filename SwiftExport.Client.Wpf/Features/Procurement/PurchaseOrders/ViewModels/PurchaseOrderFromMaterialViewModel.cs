using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

/// <summary>在一个采购弹窗内协调物料选择、采购预览调整和执行结果接纳。</summary>
public sealed partial class PurchaseOrderFromMaterialViewModel : ViewModelBaseEx, IDisposable
{
    private readonly PurchaseCandidateSelectionViewModel _picker;
    private readonly PurchaseDraftWorkflowViewModel _preview;
    private bool _disposed;

    /// <summary>初始化统一采购流程并订阅子状态变化。</summary>
    public PurchaseOrderFromMaterialViewModel(
        PurchaseCandidateSelectionViewModel picker,
        PurchaseDraftWorkflowViewModel preview)
    {
        _picker = picker;
        _preview = preview;
        _preview.PropertyChanged += OnPreviewPropertyChanged;
        Title = "从物料需求建立采购草稿";
    }

    /// <summary>采购候选选择状态。</summary>
    public PurchaseCandidateSelectionViewModel Picker => _picker;
    /// <summary>采购预览、创建与结果状态。</summary>
    public PurchaseDraftWorkflowViewModel Preview => _preview;
    /// <summary>本次是否已经产生成功或幂等已应用结果。</summary>
    public bool HasAppliedResults => _preview.HasAppliedResults;
    /// <summary>采购写命令是否正在执行。</summary>
    public bool IsWriting => _preview.IsCreateInProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectionStage))]
    [NotifyPropertyChangedFor(nameof(IsPreviewStage))]
    [NotifyPropertyChangedFor(nameof(IsResultStage))]
    public partial PurchaseOrderFromMaterialStage Stage { get; private set; }

    [ObservableProperty]
    public partial string SelectionValidationText { get; private set; } = string.Empty;

    /// <summary>当前是否处于物料选择阶段。</summary>
    public bool IsSelectionStage => Stage == PurchaseOrderFromMaterialStage.Selection;
    /// <summary>当前是否处于采购预览阶段。</summary>
    public bool IsPreviewStage => Stage == PurchaseOrderFromMaterialStage.Preview;
    /// <summary>当前是否处于创建结果阶段。</summary>
    public bool IsResultStage => Stage == PurchaseOrderFromMaterialStage.Result;

    /// <summary>重新初始化统一流程；每次打开保持独立生命周期和幂等身份。</summary>
    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _preview.Close();
        Stage = PurchaseOrderFromMaterialStage.Selection;
        SelectionValidationText = string.Empty;
        await _picker.OpenAsync(cancellationToken);
    }

    /// <summary>保留当前选择并进入预览；预览失败时仍停留在预览阶段供用户修正。</summary>
    [RelayCommand]
    private async Task ContinueToPreviewAsync()
    {
        var rows = _picker.SelectedRows;
        if (rows.Count == 0)
        {
            SelectionValidationText = "请至少选择一条待采购物料。";
            return;
        }

        SelectionValidationText = string.Empty;
        Stage = PurchaseOrderFromMaterialStage.Preview;
        await _preview.OpenAsync(new PurchaseDraftPreviewOpenRequest
        {
            EntryMode = PurchaseDraftEntryModeEnum.PurchaseWorkspace,
            Rows = rows
        });
    }

    /// <summary>从预览返回选择阶段，保留候选与勾选，便于继续调整。</summary>
    [RelayCommand]
    private void BackToSelection()
    {
        if (IsWriting) return;
        Stage = PurchaseOrderFromMaterialStage.Selection;
    }

    /// <summary>创建命令执行后切换到结果阶段；成功与部分失败均保留逐来源结果。</summary>
    [RelayCommand]
    private async Task CreateAsync()
    {
        if (!_preview.CreateCommand.CanExecute(null)) return;
        await _preview.CreateCommand.ExecuteAsync(null);
        if (_preview.Results.Count > 0)
            Stage = PurchaseOrderFromMaterialStage.Result;
    }

    /// <summary>结果存在失败项时回到预览；沿用本次执行身份，成功来源不会被重复创建。</summary>
    [RelayCommand]
    private void ReturnToPreview()
    {
        if (IsWriting) return;
        Stage = PurchaseOrderFromMaterialStage.Preview;
    }

    /// <summary>把子流程的写入与结果状态提升给 Dialog 关闭门禁和调用方。</summary>
    private void OnPreviewPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PurchaseDraftWorkflowViewModel.HasAppliedResults))
            OnPropertyChanged(nameof(HasAppliedResults));
        if (e.PropertyName == nameof(PurchaseDraftWorkflowViewModel.IsCreateInProgress))
            OnPropertyChanged(nameof(IsWriting));
    }

    /// <summary>关闭弹窗时取消查询、解除选择事件并重置预览；写命令由外层关闭门禁保护。</summary>
    public void Close()
    {
        _picker.Close();
        _preview.Close();
    }

    /// <summary>释放统一流程与子状态订阅。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _preview.PropertyChanged -= OnPreviewPropertyChanged;
        Close();
    }
}
