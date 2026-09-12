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
        _picker.InputChanged += OnPickerInputChanged;
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
    /// <summary>当前会话是否可安全关闭；写入中或结果未知时均拒绝关闭。</summary>
    public bool CanClose => _preview.CanClose;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectionStage))]
    [NotifyPropertyChangedFor(nameof(IsPreviewStage))]
    [NotifyPropertyChangedFor(nameof(IsResultStage))]
    public partial PurchaseOrderFromMaterialStage Stage { get; private set; }

    [ObservableProperty]
    public partial string SelectionValidationText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreparationActionText))]
    public partial bool HasPreparationAttempted { get; private set; }

    /// <summary>首次与失败后的重复校验共用一个明确入口，旧阻断不会禁用恢复动作。</summary>
    public string PreparationActionText => HasPreparationAttempted
        ? "重新校验所选物料"
        : "校验所选物料并进入审核";

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
        _preview.BeginSession();
        Stage = PurchaseOrderFromMaterialStage.Selection;
        SelectionValidationText = string.Empty;
        HasPreparationAttempted = false;
        _picker.SetInputEnabled(true);
        await _picker.OpenAsync(cancellationToken);
    }

    /// <summary>先在准备阶段验证全部勾选输入；只有有效且强提示已确认时才进入单据审核。</summary>
    [RelayCommand(CanExecute = nameof(CanNavigateWithinDialog))]
    private async Task ContinueToPreviewAsync()
    {
        if (!CanNavigateWithinDialog()) return;
        HasPreparationAttempted = true;
        if (!_picker.TryCommitPendingEdits())
        {
            SelectionValidationText = "当前单元格输入格式无效，请修正后重新校验所选物料。";
            return;
        }

        var rows = _picker.SelectedRows;
        if (rows.Count == 0)
        {
            SelectionValidationText = "请至少选择一条待采购物料。";
            return;
        }

        SelectionValidationText = string.Empty;
        _picker.SetInputEnabled(false);
        try
        {
            await _preview.UpdateSelectionAsync(new PurchaseDraftPreviewOpenRequest
            {
                EntryMode = PurchaseDraftEntryModeEnum.PurchaseWorkspace,
                Rows = rows
            });
            _picker.ApplyValidation(_preview.Warnings.ToArray());
            if (!_preview.HasValidPreparation)
            {
                SelectionValidationText = _preview.CreateBlockReason;
                return;
            }

            Stage = PurchaseOrderFromMaterialStage.Preview;
        }
        finally
        {
            if (Stage == PurchaseOrderFromMaterialStage.Selection)
                _picker.SetInputEnabled(true);
        }
    }

    /// <summary>从预览返回选择阶段，保留候选与勾选，便于继续调整。</summary>
    [RelayCommand(CanExecute = nameof(CanNavigateWithinDialog))]
    private void BackToSelection()
    {
        if (!CanNavigateWithinDialog()) return;
        _picker.SetInputEnabled(true);
        Stage = PurchaseOrderFromMaterialStage.Selection;
    }

    /// <summary>创建命令执行后切换到结果阶段；成功与部分失败均保留逐来源结果。</summary>
    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        if (!_preview.CreateCommand.CanExecute(null)) return;
        await _preview.CreateCommand.ExecuteAsync(null);
        if (_preview.HasUnknownCreateResult)
        {
            Stage = PurchaseOrderFromMaterialStage.Preview;
            return;
        }
        if (_preview.Results.Count > 0)
        {
            var hasFailure = _preview.Results.Any(result =>
                result.Outcome is not NetDemandCommandOutcomeEnum.Succeeded
                    and not NetDemandCommandOutcomeEnum.AlreadyApplied);
            Stage = hasFailure ? PurchaseOrderFromMaterialStage.Selection : PurchaseOrderFromMaterialStage.Result;
            if (hasFailure)
            {
                _picker.SetInputEnabled(true);
                SelectionValidationText = "采购草稿保存未完成，请核对提示并重新验证当前采购准备。";
            }
        }
    }

    /// <summary>结果存在失败项时回到预览；沿用本次执行身份，成功来源不会被重复创建。</summary>
    [RelayCommand(CanExecute = nameof(CanNavigateWithinDialog))]
    private void ReturnToPreview()
    {
        if (!CanNavigateWithinDialog()) return;
        Stage = PurchaseOrderFromMaterialStage.Preview;
    }

    /// <summary>把子流程的写入与结果状态提升给 Dialog 关闭门禁和调用方。</summary>
    private void OnPreviewPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PurchaseDraftWorkflowViewModel.HasAppliedResults))
            OnPropertyChanged(nameof(HasAppliedResults));
        if (e.PropertyName == nameof(PurchaseDraftWorkflowViewModel.IsCreateInProgress))
        {
            OnPropertyChanged(nameof(IsWriting));
            OnPropertyChanged(nameof(CanClose));
            ContinueToPreviewCommand.NotifyCanExecuteChanged();
            BackToSelectionCommand.NotifyCanExecuteChanged();
            ReturnToPreviewCommand.NotifyCanExecuteChanged();
            CreateCommand.NotifyCanExecuteChanged();
        }
        if (e.PropertyName == nameof(PurchaseDraftWorkflowViewModel.CanCreateCurrent)
            || e.PropertyName == nameof(PurchaseDraftWorkflowViewModel.HasUnknownCreateResult))
        {
            CreateCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanClose));
            ContinueToPreviewCommand.NotifyCanExecuteChanged();
            BackToSelectionCommand.NotifyCanExecuteChanged();
            ReturnToPreviewCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanNavigateWithinDialog() => !IsWriting && !_preview.HasUnknownCreateResult;

    private bool CanCreate() => !IsWriting && _preview.CreateCommand.CanExecute(null);

    private void OnPickerInputChanged(object? sender, EventArgs e)
    {
        SelectionValidationText = string.Empty;
        _preview.InvalidatePreparation();
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
        _picker.InputChanged -= OnPickerInputChanged;
        Close();
    }
}
