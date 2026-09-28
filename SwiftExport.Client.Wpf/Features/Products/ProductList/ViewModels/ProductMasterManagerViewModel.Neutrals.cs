using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Mvvm;
using System.ComponentModel;
using System.IO;

namespace SwiftExportClient.Features.Products.ProductList.ViewModels;

/// <summary>管理页中性产品区域：按需读取、独立保存及主从选择保护。</summary>
public sealed partial class ProductMasterManagerViewModel
{
    private CancellationTokenSource? _neutralCts;
    private long _neutralSequence;
    private ProductMasterLineModel? _neutralMaster;
    private bool _neutralLoaded;
    private bool _neutralLoadAttempted;

    /// <summary>由宿主视图提交原生编辑事务；关闭和业务操作前阻止无法转换的单元格输入。</summary>
    public Func<bool>? CommitPendingEdits { get; set; }

    /// <summary>获取独立于主型号脏状态的中性产品编辑集合。</summary>
    public ProductNeutralListModel Neutrals { get; } = new();

    /// <summary>获取当前明细实际所属主型号，取消选择时用于恢复原活动行。</summary>
    public ProductMasterLineModel? NeutralMaster => _neutralMaster;

    /// <summary>获取页面是否允许交互，写入和确认过程中冻结主从表。</summary>
    public bool IsInteractionEnabled => !_disposed && IsNotBusy && !IsQuerying && !IsSelectionPrompt;

    /// <summary>获取下表是否已有可编辑的持久化主型号明细。</summary>
    public bool CanEditNeutrals => IsInteractionEnabled && CanWrite && !IsNeutralLoading && _neutralLoaded
        && ReferenceEquals(SelectedMaster, _neutralMaster)
        && _neutralMaster is { IsNew: false, IsDeleted: false } && Masters.Items.Contains(_neutralMaster);

    /// <summary>获取或设置当前中性产品行。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteNeutralCommand))]
    public partial ProductNeutralLineModel? SelectedNeutral { get; set; }

    /// <summary>获取或设置颜色编辑候选。</summary>
    [ObservableProperty] public partial ProductColorDto? EditColor { get; set; }

    /// <summary>获取或设置下表格子选择模式，与上表独立。</summary>
    [ObservableProperty] public partial bool IsNeutralCellSelectionMode { get; set; }

    /// <summary>获取明细加载状态。</summary>
    [ObservableProperty] public partial bool IsNeutralLoading { get; private set; }

    /// <summary>获取当前是否正在确认上下文切换。</summary>
    [ObservableProperty] public partial bool IsSelectionPrompt { get; private set; }

    /// <summary>获取下表加载、保存或不可编辑原因。</summary>
    [ObservableProperty] public partial string NeutralStatusText { get; private set; } = "请选择已保存的主型号。";

    partial void OnSelectedNeutralChanged(ProductNeutralLineModel? value) => Neutrals.SelectedItem = value;
    partial void OnIsNeutralLoadingChanged(bool value) => NotifyNeutralCommands();
    partial void OnIsSelectionPromptChanged(bool value) => NotifyNeutralCommands();
    partial void OnIsQueryingChanged(bool value) => NotifyNeutralCommands();

    /// <summary>原生选择路由结束后接纳目标行；取消时恢复主型号，下表不丢失编辑。</summary>
    public async Task SelectMasterAsync(ProductMasterLineModel? target)
    {
        if (_disposed || IsQuerying) return;
        if (!IsInteractionEnabled || !TryCommitPendingEdits())
        {
            SelectedMaster = _neutralMaster;
            return;
        }
        if (ReferenceEquals(target, _neutralMaster)) return;
        if (target is not null && !Masters.Items.Contains(target)) return;
        if (!await ConfirmNeutralDiscardAsync())
        {
            SelectedMaster = _neutralMaster;
            return;
        }
        if (_disposed) return;
        SelectedMaster = target;
        ResetNeutralContext(target);
        if (!IsCardView) await LoadNeutralsCoreAsync();
    }

    /// <summary>切回列表时补载隐藏期间尚未读取的主型号；已加载编辑不重新查询。</summary>
    public Task EnsureNeutralsLoadedAsync() => !IsCardView && !_neutralLoadAttempted && !IsNeutralLoading
        && IsInteractionEnabled ? LoadNeutralsCoreAsync() : Task.CompletedTask;

    /// <summary>检查视图编辑转换，未绑定视图时由模型测试直接通过。</summary>
    private bool TryCommitPendingEdits() => CommitPendingEdits?.Invoke() ?? true;

    /// <summary>只确认下表丢弃；确认期间禁止其他命令改变编辑上下文。</summary>
    private async Task<bool> ConfirmNeutralDiscardAsync()
    {
        if (!Neutrals.HasChanges) return true;
        IsSelectionPrompt = true;
        try
        {
            return await _dialogs.ConfirmAsync("放弃中性产品修改", "当前中性产品有未保存修改，继续将放弃这些修改。", "放弃修改");
        }
        finally { IsSelectionPrompt = false; }
    }

    /// <summary>替换明细上下文时取消旧请求并清空旧行，禁止旧明细被用于新主型号。</summary>
    private void ResetNeutralContext(ProductMasterLineModel? target)
    {
        CancelNeutralQuery();
        _neutralMaster = target;
        _neutralLoaded = false;
        _neutralLoadAttempted = false;
        SelectedNeutral = null;
        EditColor = null;
        Neutrals.Clear();
        NeutralStatusText = target is null ? "请选择已保存的主型号。"
            : target.IsNew ? "请先保存主型号，再维护中性产品。" : $"主型号：{target.MasterSku}，尚未加载中性产品。";
        OnPropertyChanged(nameof(NeutralMaster));
        NotifyNeutralCommands();
    }

    /// <summary>明细查询采用取消与序号双重保护；旧响应和关闭后的响应不得回写。</summary>
    private async Task LoadNeutralsCoreAsync()
    {
        var master = _neutralMaster;
        if (_disposed || master is null || master.IsNew || master.Uid == Guid.Empty || master.IsDeleted) return;
        CancelNeutralQuery();
        var sequence = ++_neutralSequence;
        using var cts = new CancellationTokenSource();
        _neutralCts = cts;
        IsNeutralLoading = true;
        _neutralLoadAttempted = true;
        _neutralLoaded = false;
        NeutralStatusText = $"正在加载 {master.MasterSku} 的中性产品…";
        try
        {
            var result = await _runner.RunAsync(() => _api.GetDetailAsync(master.Uid, cts.Token), cts.Token, "加载中性产品失败");
            if (_disposed || cts.IsCancellationRequested || sequence != _neutralSequence || !ReferenceEquals(master, _neutralMaster)) return;
            if (!result.Success || result.Data is null)
            {
                NeutralStatusText = "中性产品加载失败，请点击下方刷新重试。";
                if (!string.IsNullOrWhiteSpace(result.Message)) _messages.ShowError("加载中性产品失败", result.Message);
                return;
            }
            AcceptNeutrals(result.Data);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_neutralCts, cts))
            {
                _neutralCts = null;
                IsNeutralLoading = false;
                NotifyNeutralCommands();
            }
        }
    }

    /// <summary>接纳服务端明细和展示投影，不接纳主型号其他未保存字段。</summary>
    private void AcceptNeutrals(ProductMasterDetailDto detail)
    {
        if (_neutralMaster?.Uid != detail.Uid) return;
        Neutrals.LoadFromDtos(detail.Neutrals ?? []);
        SelectedNeutral = Neutrals.Items.FirstOrDefault();
        _neutralLoaded = true;
        var main = detail.Neutrals?.FirstOrDefault(item => item.Data.IsMainImage)?.Data;
        using (_neutralMaster.SuppressChangeTracking())
        {
            _neutralMaster.MainImagePath = main?.ImagePath ?? string.Empty;
            _neutralMaster.DefaultSalesPriceUsd = main?.DefaultSalesPriceUsd;
            _neutralMaster.DefaultPurchasePriceCny = main?.DefaultPurchasePriceCny;
        }
        NeutralStatusText = $"{_neutralMaster.MasterSku} · {Neutrals.Items.Count} 条中性产品";
        ValidationSummary = null;
        UpdateChangeState();
    }

    /// <summary>新增行只归属于已加载的持久化主型号。</summary>
    [RelayCommand(CanExecute = nameof(CanAddNeutral))]
    private void AddNeutral()
    {
        if (!CanAddNeutral() || !TryCommitPendingEdits()) return;
        SelectedNeutral = Neutrals.AddNewNeutral(_neutralMaster!.Uid);
    }

    /// <summary>新增行直接移除；已保存行确认软删除，完成后定位剩余末行。</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteNeutral))]
    private async Task DeleteNeutralAsync()
    {
        if (!CanDeleteNeutral() || !TryCommitPendingEdits()) return;
        var lines = Neutrals.CheckedItems.Count > 0 ? Neutrals.CheckedItems.ToArray()
            : SelectedNeutral is null ? [] : [SelectedNeutral];
        IsSelectionPrompt = true;
        try
        {
            if (lines.Any(line => !line.IsNew)
                && !await _dialogs.ConfirmAsync("删除中性产品", $"确定删除选中的 {lines.Length} 条中性产品吗？保存后生效。", "删除")) return;
            if (_disposed) return;
            foreach (var line in lines) Neutrals.Remove(line);
            SelectedNeutral = Neutrals.Items.LastOrDefault();
        }
        finally { IsSelectionPrompt = false; }
    }

    /// <summary>独立提交明细；失败保留编辑，成功仅接纳下表及主图价格投影。</summary>
    [RelayCommand(CanExecute = nameof(CanSaveNeutrals))]
    private async Task SaveNeutralsAsync()
    {
        if (!CanSaveNeutrals() || !TryCommitPendingEdits()) return;
        await RunWriteAsync(async () =>
        {
            var error = await Neutrals.ValidateAllAsync();
            if (!string.IsNullOrWhiteSpace(error))
            {
                ValidationSummary = error;
                _messages.ShowWarning("中性产品未保存", error);
                return;
            }
            var result = await _runner.RunAsync(() => _neutralApi.BatchSaveAsync(_neutralMaster!.Uid, Neutrals.CreateSaveRequest()), "保存中性产品失败");
            if (_disposed) return;
            if (!result.Success || result.Data is null)
            {
                ValidationSummary = result.Message;
                if (result.Error?.ErrorCodeIs(ServiceErrorCodes.VersionConflict) == true)
                    _messages.ShowWarning("版本冲突", "中性产品已被其他用户修改，本地编辑已保留，请决定是否刷新。");
                else _messages.ShowError("保存中性产品失败", result.Message);
                return;
            }
            AcceptNeutrals(result.Data);
            _messages.ShowSuccess("中性产品已保存", NeutralStatusText);
        }, "正在保存中性产品…");
    }

    /// <summary>只重新读取下表；不改变主型号编辑基线。</summary>
    [RelayCommand(CanExecute = nameof(CanRefreshNeutrals))]
    private async Task RefreshNeutralsAsync()
    {
        if (!CanRefreshNeutrals() || !TryCommitPendingEdits() || !await ConfirmNeutralDiscardAsync()) return;
        ResetNeutralContext(_neutralMaster);
        await LoadNeutralsCoreAsync();
    }

    /// <summary>仅撤销下表行变更。</summary>
    [RelayCommand(CanExecute = nameof(CanSaveNeutrals))]
    private void RejectNeutralChanges()
    {
        if (!CanSaveNeutrals() || !TryCommitPendingEdits()) return;
        Neutrals.RejectChanges();
        SelectedNeutral = Neutrals.Items.LastOrDefault();
    }

    /// <summary>颜色候选确认后回写业务标识和名称。</summary>
    [RelayCommand]
    private void CommitColor(ProductNeutralLineModel? line)
    {
        if (!CanEditNeutrals || line is null || EditColor is null) return;
        line.ColorId = EditColor.Uid;
        line.ColorName = EditColor.ColorNameZh ?? EditColor.ColorNameEn ?? string.Empty;
        EditColor = null;
    }

    /// <summary>沿用详情页主图切换规则，勾选新行时取消其他默认主图。</summary>
    [RelayCommand]
    private void SetMainImage(ProductNeutralLineModel? line)
    {
        if (CanEditNeutrals && line is not null) Neutrals.SetMainImage(line);
    }

    /// <summary>从配置文件读取图片父文件夹,与产品代码组合生成完整图片路径。</summary>
    [RelayCommand]
    private void ReBuildImagePath(ProductNeutralLineModel? line)
    {
        if (!CanEditNeutrals || line is null) return;

        if (string.IsNullOrWhiteSpace(line.ProductCode)) return;

        string imgFolder = _productOptions.Value.BaseImageFolder;
        if (imgFolder == null) return;
        string fileName = Path.ChangeExtension(line.ProductCode, ".jpg");
        line.ImagePath = Path.Combine(imgFolder, fileName);
    }

    private bool CanAddNeutral() => CanEditNeutrals;
    private bool CanDeleteNeutral() => CanEditNeutrals && (Neutrals.CheckedCount > 0 || SelectedNeutral is not null);
    private bool CanSaveNeutrals() => CanEditNeutrals && Neutrals.HasChanges;
    private bool CanRefreshNeutrals() => IsInteractionEnabled && !IsNeutralLoading
        && ReferenceEquals(SelectedMaster, _neutralMaster) && _neutralMaster is { IsNew: false, IsDeleted: false };

    /// <summary>主从任一集合变化均更新文档关闭保护。</summary>
    private void OnNeutralListStateChanged(object? sender, ListStateChangedEventArgs<ProductNeutralLineModel> e) => UpdateChangeState();

    /// <summary>接收基类 Busy 通知，确保两张表在写入期间同时冻结。</summary>
    private void OnManagerStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IsBusy) or nameof(CanWrite))
        {
            NotifyNeutralCommands();
            NotifySyncCommands();
        }
    }

    /// <summary>统一刷新上下文可编辑状态与相关命令。</summary>
    private void NotifyNeutralCommands()
    {
        OnPropertyChanged(nameof(IsInteractionEnabled));
        OnPropertyChanged(nameof(CanEditNeutrals));
        AddNeutralCommand.NotifyCanExecuteChanged();
        DeleteNeutralCommand.NotifyCanExecuteChanged();
        SaveNeutralsCommand.NotifyCanExecuteChanged();
        RefreshNeutralsCommand.NotifyCanExecuteChanged();
        RejectNeutralChangesCommand.NotifyCanExecuteChanged();
    }

    /// <summary>取消在途明细查询；CTS 由所属请求 finally 释放。</summary>
    private void CancelNeutralQuery()
    {
        ++_neutralSequence;
        var cts = _neutralCts;
        _neutralCts = null;
        cts?.Cancel();
        IsNeutralLoading = false;
    }
}
