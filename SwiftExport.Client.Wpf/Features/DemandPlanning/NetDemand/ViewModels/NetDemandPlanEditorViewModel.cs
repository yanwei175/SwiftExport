using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;

public sealed partial class NetDemandPlanEditorViewModel : ViewModelBaseEx, IDisposable
{
    private readonly INetDemandWorkspaceApi _api;
    private readonly ISupplierApi _supplierApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private long _supplierSearchVersion;
    private IReadOnlyList<NetDemandRowModel> _batchRows = [];
    private bool _loading;
    private bool _disposed;

    public NetDemandPlanEditorViewModel(
        INetDemandWorkspaceApi api,
        ISupplierApi supplierApi,
        IApiCallRunner apiCallRunner,
        IMessageService messageService)
    {
        _api = api;
        _supplierApi = supplierApi;
        _apiCallRunner = apiCallRunner;
        _messageService = messageService;
        Title = "修改净需求计划";
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(SearchSuppliersCommand);
    }

    public event EventHandler? Saved;

    public ObservableCollection<SupplierDto> SupplierCandidates { get; } = [];
    public ObservableCollection<NetDemandListItemDto> RelatedCustomerSuppliedPlans { get; } = [];
    public ObservableCollection<NetDemandExecutionLinkDto> BlockingExecutionLinks { get; } = [];

    public IReadOnlyList<NetDemandFilterOption<DemandExecutionTypeEnum>> ExecutionTypeOptions { get; } =
    [
        new(DemandExecutionTypeEnum.Purchase, "采购"),
        new(DemandExecutionTypeEnum.Production, "生产（执行工作台待接入）"),
        new(DemandExecutionTypeEnum.Outsourcing, "外协（执行工作台待接入）"),
    ];

    public bool IsPurchase => ExecutionType == DemandExecutionTypeEnum.Purchase;
    public bool HasUnsavedChanges => IsOpen && IsDirty;
    public bool HasRelatedCustomerSuppliedPlans => RelatedCustomerSuppliedPlans.Count > 0;
    public bool HasBlockingExecutionLinks => BlockingExecutionLinks.Count > 0;
    public string RelatedPlansSummary => HasRelatedCustomerSuppliedPlans
        ? $"将影响 {RelatedCustomerSuppliedPlans.Count} 条客供件送达目标"
        : "没有关联客供件计划";
    public string BlockersSummary => HasBlockingExecutionLinks
        ? $"存在 {BlockingExecutionLinks.Count} 条活动执行关系，必须先取消安全采购草稿或完成反向处理"
        : "没有活动执行阻断";

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    public partial bool IsBatchMode { get; private set; }

    [ObservableProperty]
    public partial Guid AllocationId { get; private set; }

    [ObservableProperty]
    public partial int AllocationVersion { get; private set; }

    [ObservableProperty]
    public partial string SourceSummary { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Sku { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string DemandRoleText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial decimal PlannedQty { get; private set; }

    [ObservableProperty]
    public partial DateTime? DemandRequiredDate { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPurchase))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial DemandExecutionTypeEnum ExecutionType { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial DateTime? PlannedDeliveryDate { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial SupplierDto? SelectedSupplier { get; set; }

    [ObservableProperty]
    public partial string SupplierKeyword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSupplierLookupBusy { get; private set; }

    [ObservableProperty]
    public partial string DeliveryTargetSummary { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string Reason { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusText { get; private set; } = string.Empty;

    public void OpenSingle(NetDemandDetailDto detail, bool isReadOnly)
    {
        var summary = detail.Summary ?? throw new InvalidOperationException("净需求详情缺少 Summary。");
        var allocation = detail.ActiveAllocation ?? throw new InvalidOperationException("净需求详情缺少活动计划分配。");
        _loading = true;
        try
        {
            IsReadOnly = isReadOnly;
            IsBatchMode = false;
            _batchRows = [];
            AllocationId = allocation.Uid;
            AllocationVersion = allocation.Version;
            SourceSummary = $"{summary.SourceNo} / 行 {summary.SourceLineNumber?.ToString() ?? "-"}";
            Sku = summary.Sku ?? string.Empty;
            DemandRoleText = summary.IsCustomerSupplied ? "客供件" : "父件";
            PlannedQty = allocation.PlannedQty;
            DemandRequiredDate = allocation.DemandRequiredDate;
            ExecutionType = allocation.ExecutionType;
            PlannedDeliveryDate = allocation.PlannedDeliveryDate;
            SelectedSupplier = CreateCurrentSupplier(allocation);
            DeliveryTargetSummary = FormatDeliveryTarget(allocation);
            ReplaceRelatedPlans(detail.RelatedCustomerSuppliedPlans);
            ReplaceBlockers(detail.BlockingExecutionLinks);
            Reason = string.Empty;
            StatusText = BlockingExecutionLinks.Count == 0
                ? "修改将生成新的计划版本并保留历史。"
                : "当前计划存在活动执行关系，不能保存。请先取消安全采购草稿；若已进入正式执行，请完成反向处理。";
            IsOpen = true;
            IsDirty = false;
            ValidationSummary = null;
        }
        finally
        {
            _loading = false;
        }

        SaveCommand.NotifyCanExecuteChanged();
    }

    public void OpenBatch(IReadOnlyList<NetDemandRowModel> rows, bool isReadOnly)
    {
        if (rows.Count == 0)
        {
            throw new ArgumentException("批量分配供应商至少需要一条净需求。", nameof(rows));
        }

        _loading = true;
        try
        {
            IsReadOnly = isReadOnly;
            IsBatchMode = true;
            _batchRows = rows.ToArray();
            AllocationId = Guid.Empty;
            AllocationVersion = 0;
            SourceSummary = $"已选择 {rows.Count} 条净需求";
            Sku = rows.Count == 1 ? rows[0].Sku : $"{rows.Select(row => row.Sku).Distinct(StringComparer.OrdinalIgnoreCase).Count()} 个 SKU";
            DemandRoleText = rows[0].DemandRoleText;
            PlannedQty = rows.Sum(row => row.PlannedQty);
            DemandRequiredDate = null;
            ExecutionType = DemandExecutionTypeEnum.Purchase;
            PlannedDeliveryDate = null;
            SelectedSupplier = null;
            DeliveryTargetSummary = rows[0].IsCustomerSupplied
                ? "客供件送达目标由服务端按各自父件计划保持不变。"
                : "标准交付";
            ReplaceRelatedPlans([]);
            ReplaceBlockers([]);
            Reason = string.Empty;
            StatusText = "批量操作只修改采购供应商；混合父件和客供件不允许提交。";
            IsOpen = true;
            IsDirty = false;
            ValidationSummary = null;
        }
        finally
        {
            _loading = false;
        }

        SaveCommand.NotifyCanExecuteChanged();
    }

    public void Close(bool discardChanges)
    {
        if (!discardChanges && HasUnsavedChanges)
        {
            return;
        }

        Interlocked.Increment(ref _supplierSearchVersion);
        _batchRows = [];
        SupplierCandidates.Clear();
        ReplaceRelatedPlans([]);
        ReplaceBlockers([]);
        SelectedSupplier = null;
        SupplierKeyword = string.Empty;
        IsSupplierLookupBusy = false;
        IsOpen = false;
        IsDirty = false;
        ValidationSummary = null;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchSuppliersAsync(string? keyword)
    {
        var version = Interlocked.Increment(ref _supplierSearchVersion);
        IsSupplierLookupBusy = true;
        var result = await _apiCallRunner.RunAsync(
            () => _supplierApi.GetPagedAsync(new SupplierSearchRequest
            {
                Keyword = keyword?.Trim(),
                PageIndex = 1,
                PageSize = 30,
            }),
            "搜索供应商失败");
        if (version != Volatile.Read(ref _supplierSearchVersion))
        {
            return;
        }

        SupplierCandidates.Clear();
        if (result.Success && result.Data is not null)
        {
            foreach (var supplier in result.Data.Items)
            {
                SupplierCandidates.Add(supplier);
            }
        }

        IsSupplierLookupBusy = false;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var validationError = ValidateForSave();
        if (validationError is not null)
        {
            ValidationSummary = validationError;
            _messageService.ShowWarning("净需求计划未保存", validationError);
            return;
        }

        await RunWriteAsync(async () =>
        {
            var result = IsBatchMode
                ? await SaveBatchAsync()
                : await SaveSingleAsync();
            if (!result.Success || result.Data is null)
            {
                _messageService.ShowError("保存净需求计划失败", result.Message);
                return;
            }

            var succeeded = result.Data.Items.Count(item => item.Outcome == NetDemandCommandOutcomeEnum.Succeeded);
            var already = result.Data.Items.Count(item => item.Outcome == NetDemandCommandOutcomeEnum.AlreadyApplied);
            var failed = result.Data.Items.Count - succeeded - already;
            var firstFailure = result.Data.Items.FirstOrDefault(item => item.Outcome == NetDemandCommandOutcomeEnum.Failed)?.Message;
            if (failed > 0)
            {
                _messageService.ShowWarning("净需求计划部分失败", $"成功 {succeeded}，已应用 {already}，失败 {failed}。{firstFailure}");
                if (succeeded + already > 0)
                {
                    IsDirty = false;
                    Saved?.Invoke(this, EventArgs.Empty);
                }
                return;
            }

            IsDirty = false;
            _messageService.ShowSuccess("净需求计划已保存", $"成功 {succeeded}，已应用 {already}。");
            Saved?.Invoke(this, EventArgs.Empty);
        }, "正在保存净需求计划...");
    }

    private Task<SwiftExport.Contracts.Wrappers.ServiceResult<NetDemandPlanCommandResult>> SaveSingleAsync()
    {
        return _apiCallRunner.RunAsync(
            () => _api.ReplacePlanAsync(new ReplaceNetDemandPlanCommand
            {
                CommandId = Guid.NewGuid(),
                AllocationId = AllocationId,
                ExpectedVersion = AllocationVersion,
                ExecutionType = ExecutionType,
                PurchaseSupplierId = IsPurchase ? SelectedSupplier?.Uid : null,
                PlannedDeliveryDate = PlannedDeliveryDate,
                Reason = Reason.Trim(),
            }),
            "修改净需求计划失败");
    }

    private Task<SwiftExport.Contracts.Wrappers.ServiceResult<NetDemandPlanCommandResult>> SaveBatchAsync()
    {
        return _apiCallRunner.RunAsync(
            () => _api.BatchAssignSupplierAsync(new BatchAssignSupplierCommand
            {
                CommandId = Guid.NewGuid(),
                SupplierId = SelectedSupplier!.Uid,
                Reason = Reason.Trim(),
                Items = _batchRows.Select((row, index) => new BatchAssignSupplierItem
                {
                    AllocationId = row.AllocationId,
                    ExpectedVersion = row.AllocationVersion,
                    ClientKey = row.AllocationId.ToString("N"),
                    RowIndex = index,
                }).ToList(),
            }),
            "批量分配供应商失败");
    }

    private bool CanSave() => CanWrite && IsNotBusy && IsOpen && !HasBlockingExecutionLinks;

    private string? ValidateForSave()
    {
        if (string.IsNullOrWhiteSpace(Reason))
        {
            return "必须填写计划变更原因。";
        }

        if ((IsBatchMode || IsPurchase) && (SelectedSupplier is null || SelectedSupplier.Uid == Guid.Empty))
        {
            return "采购计划必须选择供应商。";
        }

        if (IsBatchMode && _batchRows.Select(row => row.IsCustomerSupplied).Distinct().Count() > 1)
        {
            return "父件和客供件不能在同一次批量供应商分配中混合提交。";
        }

        return null;
    }

    private static SupplierDto? CreateCurrentSupplier(NetDemandAllocationDto allocation)
    {
        return allocation.PurchaseSupplierId is not Guid supplierId
            ? null
            : new SupplierDto
            {
                Uid = supplierId,
                SupplierCode = allocation.PurchaseSupplierCode ?? string.Empty,
                SupplierName = allocation.PurchaseSupplierName ?? string.Empty,
            };
    }

    private static string FormatDeliveryTarget(NetDemandAllocationDto allocation)
    {
        return allocation.DeliveryTargetSupplierId is null
            ? "标准交付"
            : $"{allocation.DeliveryTargetSupplierCode} {allocation.DeliveryTargetSupplierName}".Trim();
    }

    private void ReplaceRelatedPlans(IEnumerable<NetDemandListItemDto> items)
    {
        RelatedCustomerSuppliedPlans.Clear();
        foreach (var item in items)
        {
            RelatedCustomerSuppliedPlans.Add(item);
        }

        OnPropertyChanged(nameof(HasRelatedCustomerSuppliedPlans));
        OnPropertyChanged(nameof(RelatedPlansSummary));
    }

    private void ReplaceBlockers(IEnumerable<NetDemandExecutionLinkDto> items)
    {
        BlockingExecutionLinks.Clear();
        foreach (var item in items)
        {
            BlockingExecutionLinks.Add(item);
        }

        OnPropertyChanged(nameof(HasBlockingExecutionLinks));
        OnPropertyChanged(nameof(BlockersSummary));
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnExecutionTypeChanged(DemandExecutionTypeEnum value) => MarkDirty();
    partial void OnPlannedDeliveryDateChanged(DateTime? value) => MarkDirty();
    partial void OnSelectedSupplierChanged(SupplierDto? value) => MarkDirty();
    partial void OnReasonChanged(string value) => MarkDirty();

    private void MarkDirty()
    {
        if (_loading || !IsOpen)
        {
            return;
        }

        IsDirty = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Interlocked.Increment(ref _supplierSearchVersion);
    }
}
