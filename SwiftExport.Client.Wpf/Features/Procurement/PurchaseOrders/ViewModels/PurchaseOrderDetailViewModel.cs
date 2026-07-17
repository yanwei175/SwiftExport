using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Services;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

public sealed class PurchaseOrderDocumentOpenRequest
{
    public Guid PurchaseOrderId { get; set; }
}

/// <summary>采购单完整详情文档；字段覆盖旧采购管理与详情页面，但只调用新采购工作台 API。</summary>
public sealed partial class PurchaseOrderDetailViewModel : ViewModelBaseEx, IDocumentTabViewModel, IDocumentTabInitializable, IDisposable
{
    public const string RouteKey = "procurement-purchase-order-detail";
    private readonly IPurchaseOrderWorkspaceApi _api;
    private readonly IApiCallRunner _runner;
    private readonly IDialogService _dialogs;
    private readonly IMessageService _messages;
    private readonly IPurchaseOrderTraceExportService _exportService;
    private readonly INavigationService _navigation;
    private bool _loading;

    public PurchaseOrderDetailViewModel(IPurchaseOrderWorkspaceApi api, IApiCallRunner runner, IDialogService dialogs,
        IMessageService messages, IPurchaseOrderTraceExportService exportService, INavigationService navigation)
    {
        _api = api; _runner = runner; _dialogs = dialogs; _messages = messages; _exportService = exportService; _navigation = navigation;
        Title = "采购单详情";
        Header.PropertyChanged += OnEditablePropertyChanged;
        RegisterBusyAwareCommand(SaveCommand); RegisterBusyAwareCommand(ConfirmCommand); RegisterBusyAwareCommand(CancelConfirmationCommand);
        RegisterBusyAwareCommand(ReportProgressCommand); RegisterBusyAwareCommand(OpenTraceCommand); RegisterBusyAwareCommand(ExportCommand);
        RegisterBusyAwareCommand(RefreshCommand); RegisterBusyAwareCommand(CloseDocumentCommand);
    }

    public PurchaseOrderHeaderEditorModel Header { get; } = new();
    public ObservableCollection<PurchaseOrderLineEditorModel> Lines { get; } = [];
    public ObservableCollection<BusinessOperationTraceItemDto> Operations { get; } = [];
    public string DocumentTitle => string.IsNullOrWhiteSpace(Header.OrderNo) ? "采购单详情" : Header.OrderNo;
    public bool HasUnsavedChanges => IsDirty;
    public bool IsDraftEditable => Header.CanEditDraft && IsNotBusy;
    public string ArrangementSummary => $"已安排 {Lines.Count(x => x.LatestProgress?.ArrangementStatus == SupplierArrangementStatusEnum.Arranged)}/{Lines.Count}";
    public IReadOnlyList<SupplierArrangementStatusEnum> ArrangementOptions { get; } = [SupplierArrangementStatusEnum.Unknown, SupplierArrangementStatusEnum.NotArranged, SupplierArrangementStatusEnum.Arranged, SupplierArrangementStatusEnum.NotApplicable];

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ReportProgressCommand))] public partial PurchaseOrderLineEditorModel? SelectedLine { get; set; }
    [ObservableProperty] public partial string CancellationReason { get; set; } = string.Empty;
    [ObservableProperty] public partial SupplierArrangementStatusEnum ReportArrangementStatus { get; set; }
    [ObservableProperty] public partial decimal ReportReadyQty { get; set; }
    [ObservableProperty] public partial DateTime? ReportExpectedReadyDate { get; set; }
    [ObservableProperty] public partial DateTime ReportedDate { get; set; } = DateTime.Today;
    [ObservableProperty] public partial string ReportNote { get; set; } = string.Empty;
    [ObservableProperty] public partial string CorrectionReason { get; set; } = string.Empty;

    public Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken)
    {
        var request = parameter as PurchaseOrderDocumentOpenRequest;
        return request?.PurchaseOrderId is Guid uid && uid != Guid.Empty ? LoadAsync(uid, cancellationToken) : Task.CompletedTask;
    }

    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return false;
        if (!HasUnsavedChanges) return true;
        return await _dialogs.ConfirmAsync("关闭采购详情", "当前采购草稿有未保存修改，确定放弃并关闭吗？", "放弃并关闭", "继续编辑", cancellationToken);
    }

    partial void OnSelectedLineChanged(PurchaseOrderLineEditorModel? value)
    {
        var latest = value?.LatestProgress;
        ReportArrangementStatus = latest?.ArrangementStatus ?? SupplierArrangementStatusEnum.Unknown;
        ReportReadyQty = latest?.ReadyQty ?? 0m; ReportExpectedReadyDate = latest?.ExpectedReadyDate; ReportedDate = DateTime.Today;
        ReportNote = string.Empty; CorrectionReason = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!await _dialogs.ConfirmAsync("保存采购草稿", "保存商业、包装、交期、供应商 SKU、描述、价格和备注；不会修改数量、供应商或需求来源。", "保存")) return;
        await RunWriteAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.UpdateDraftAsync(Header.Uid, CreateUpdateCommand()), "保存采购草稿失败");
            if (!result.Success) { _messages.ShowError("保存失败", result.Message); return; }
            _messages.ShowSuccess("采购草稿已保存", Header.OrderNo); await LoadAsync(Header.Uid, CancellationToken.None);
        }, "正在保存采购草稿...");
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        if (!await _dialogs.ConfirmAsync("确认采购单", "确认前将校验供应商、含税价格、需求 Link 和客供件送达关系。", "确认")) return;
        await RunWriteAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.ConfirmAsync(Header.Uid, new ConfirmPurchaseOrderCommand { CommandId = Guid.NewGuid(), ExpectedVersion = Header.Version }), "确认采购单失败");
            if (!result.Success) { _messages.ShowError("确认失败", result.Message); return; }
            _messages.ShowSuccess("采购单已确认", Header.OrderNo); await LoadAsync(Header.Uid, CancellationToken.None);
        }, "正在确认采购单...");
    }

    [RelayCommand(CanExecute = nameof(CanCancelConfirmation))]
    private async Task CancelConfirmationAsync()
    {
        if (string.IsNullOrWhiteSpace(CancellationReason)) { _messages.ShowWarning("需要原因", "请填写取消确认原因。"); return; }
        if (!await _dialogs.ConfirmAsync("取消采购确认", "系统会检查收货和库存事实。是否继续？", "取消确认")) return;
        await RunWriteAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.CancelConfirmationAsync(Header.Uid, new CancelPurchaseOrderConfirmationCommand { CommandId = Guid.NewGuid(), ExpectedVersion = Header.Version, Reason = CancellationReason.Trim() }), "取消采购确认失败");
            if (!result.Success) { _messages.ShowError("取消确认失败", result.Message); return; }
            CancellationReason = string.Empty; _messages.ShowSuccess("已取消确认", Header.OrderNo); await LoadAsync(Header.Uid, CancellationToken.None);
        }, "正在取消采购确认...");
    }

    [RelayCommand(CanExecute = nameof(CanReportProgress))]
    private async Task ReportProgressAsync()
    {
        var line = SelectedLine; if (line is null) return;
        if (ReportReadyQty < line.LatestReadyQty && string.IsNullOrWhiteSpace(CorrectionReason)) { _messages.ShowWarning("需要纠错原因", "累计已备好数量下降时必须填写纠错原因。"); return; }
        if (!await _dialogs.ConfirmAsync("登记供应商反馈", $"为第 {line.LineNumber} 行新增累计已备好 {ReportReadyQty:N4} 的不可覆盖记录。", "登记")) return;
        await RunWriteAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.ReportProgressAsync(Header.Uid, new SupplierProgressReportCommand
            {
                CommandId = Guid.NewGuid(), PurchaseOrderLineId = line.Uid, ExpectedPurchaseOrderVersion = Header.Version,
                ExpectedLineVersion = line.Version, ArrangementStatus = ReportArrangementStatus, ReadyQty = ReportReadyQty,
                ExpectedReadyDate = ReportExpectedReadyDate, SupplierReportedDate = ReportedDate, Note = ReportNote, CorrectionReason = CorrectionReason
            }), "登记供应商反馈失败");
            if (!result.Success) { _messages.ShowError("登记失败", result.Message); return; }
            _messages.ShowSuccess("供应商反馈已登记", line.Sku); await LoadAsync(Header.Uid, CancellationToken.None);
        }, "正在登记供应商反馈...");
    }

    [RelayCommand] private Task RefreshAsync() => LoadAsync(Header.Uid, CancellationToken.None);

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private async Task OpenTraceAsync()
    {
        var result = await _runner.RunAsync(() => _api.GetTraceAsync(Header.Uid), "加载采购追溯失败");
        if (result.Success && result.Data is not null) await _dialogs.ShowContentAsync($"采购追溯 · {Header.OrderNo}", new Views.PurchaseOrderTraceView { DataContext = result.Data });
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private async Task ExportAsync()
    {
        var result = await _runner.RunAsync(() => _api.GetTraceAsync(Header.Uid), "加载采购追溯失败");
        if (!result.Success || result.Data is null) { _messages.ShowError("导出失败", result.Message); return; }
        var path = await _exportService.ExportAsync(result.Data); if (path is not null) _messages.ShowSuccess("导出完成", path);
    }

    [RelayCommand] private Task CloseDocumentAsync() => _navigation.CloseAsync(_navigation.OpenTabs.FirstOrDefault(x => ReferenceEquals(x.ContentViewModel, this)));

    private async Task LoadAsync(Guid uid, CancellationToken cancellationToken)
    {
        if (uid == Guid.Empty) return;
        await RunBusyAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.GetDetailAsync(uid, cancellationToken), cancellationToken, "加载采购详情失败");
            if (!result.Success || result.Data is null) { _messages.ShowError("加载采购详情失败", result.Message); return; }
            Load(result.Data);
        }, "正在加载采购详情...");
    }

    private void Load(PurchaseOrderDetailDto dto)
    {
        _loading = true;
        foreach (var line in Lines) line.PropertyChanged -= OnEditablePropertyChanged;
        Lines.Clear(); Operations.Clear(); Header.Load(dto);
        foreach (var item in dto.Lines.Select(PurchaseOrderLineEditorModel.FromDto)) { item.PropertyChanged += OnEditablePropertyChanged; Lines.Add(item); }
        foreach (var operation in dto.Operations) Operations.Add(operation);
        SelectedLine = Lines.FirstOrDefault(); _loading = false; IsDirty = false;
        OnPropertyChanged(nameof(DocumentTitle)); OnPropertyChanged(nameof(HasUnsavedChanges)); OnPropertyChanged(nameof(IsDraftEditable)); OnPropertyChanged(nameof(ArrangementSummary));
        NotifyCommands();
    }

    private UpdatePurchaseOrderDraftCommand CreateUpdateCommand() => new()
    {
        CommandId = Guid.NewGuid(), ExpectedVersion = Header.Version, UpdateLegacyHeaderFields = true, BoNo = Header.BoNo, CustomerPoNo = Header.CustomerPoNo,
        SalesPerson = Header.SalesPerson, PurchasePerson = Header.PurchasePerson, ReplyDate = Header.ReplyDate,
        InletHoseType = Header.InletHoseType, AeratorType = Header.AeratorType, PalletType = Header.PalletType, BoxType = Header.BoxType,
        LabelType = Header.LabelType, InstructionManualType = Header.InstructionManualType, CartonType = Header.CartonType,
        LogoType = Header.LogoType, DiscountRemark = Header.DiscountRemark, Remark = Header.Remark,
        Lines = Lines.Select(x => new UpdatePurchaseOrderDraftLineCommand { PurchaseOrderLineId = x.Uid, ExpectedVersion = x.Version,
            PlannedDeliveryDate = x.PlannedDeliveryDate, SupplierSku = x.SupplierSku, DescriptionZh = x.DescriptionZh,
            UnitPrice = x.UnitPrice, TaxPrice = x.TaxPrice, SubNote = x.SubNote }).ToList()
    };

    private void OnEditablePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading || !Header.CanEditDraft) return;
        IsDirty = true; OnPropertyChanged(nameof(HasUnsavedChanges)); NotifyCommands();
    }

    private bool CanSave() => CanWrite && IsNotBusy && Header.CanEditDraft && IsDirty && Lines.Count > 0;
    private bool CanConfirm() => CanWrite && IsNotBusy && Header.CanConfirm && !IsDirty;
    private bool CanCancelConfirmation() => CanWrite && IsNotBusy && Header.CanCancelConfirmation && !IsDirty;
    private bool CanReportProgress() => CanWrite && IsNotBusy && Header.CanReportProgress && SelectedLine is not null && !IsDirty;
    private bool CanUseDocument() => IsNotBusy && Header.Uid != Guid.Empty;
    private void NotifyCommands()
    {
        SaveCommand.NotifyCanExecuteChanged(); ConfirmCommand.NotifyCanExecuteChanged(); CancelConfirmationCommand.NotifyCanExecuteChanged();
        ReportProgressCommand.NotifyCanExecuteChanged(); OpenTraceCommand.NotifyCanExecuteChanged(); ExportCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        Header.PropertyChanged -= OnEditablePropertyChanged;
        foreach (var line in Lines) line.PropertyChanged -= OnEditablePropertyChanged;
    }
}
