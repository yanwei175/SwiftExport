using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.Models;

public sealed class PurchaseDraftPreviewOpenRequest
{
    public PurchaseDraftEntryModeEnum EntryMode { get; init; }
    public IReadOnlyList<MaterialDemandWorkspaceRowModel> Rows { get; init; } = [];
    public string SourceType { get; init; } = string.Empty;
    public Guid? SourceUid { get; init; }
    public string SourceNo { get; init; } = string.Empty;
}

public sealed partial class PurchaseDraftSelectionItemModel : ObservableObject
{
    public Guid CommandItemId { get; init; } = Guid.NewGuid();
    public Guid NetDemandId { get; init; }
    public int ExpectedVersion { get; init; }
    public decimal ExpectedRemainingQty { get; init; }
    public Guid SupplierId { get; init; }
    public Guid? DeliveryTargetSupplierId { get; init; }
    public string SourceType { get; init; } = string.Empty;
    public Guid SourceUid { get; init; }
    public string SourceNo { get; init; } = string.Empty;
    public string Sku { get; init; } = string.Empty;
    public bool IsQuantityReadOnly { get; init; }
    public string? QuantityError => PurchaseQty <= 0 || PurchaseQty > ExpectedRemainingQty
        ? $"本次数量必须大于 0 且不超过 {ExpectedRemainingQty:N4}。"
        : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QuantityError))]
    public partial decimal PurchaseQty { get; set; }

    public event EventHandler? ValueChanged;

    partial void OnPurchaseQtyChanged(decimal value) => ValueChanged?.Invoke(this, EventArgs.Empty);

    public PurchaseDraftPreviewItem ToDto() => new()
    {
        CommandItemId = CommandItemId,
        NetDemandId = NetDemandId,
        ExpectedVersion = ExpectedVersion,
        ExpectedRemainingQty = ExpectedRemainingQty,
        PurchaseQty = PurchaseQty,
        SupplierId = SupplierId,
        DeliveryTargetSupplierId = DeliveryTargetSupplierId,
    };
}

public sealed class PurchaseDraftTargetChoiceModel
{
    public PurchaseDraftTargetModeEnum TargetMode { get; init; }
    public Guid? ExistingPurchaseOrderId { get; init; }
    public int? ExpectedPurchaseOrderVersion { get; init; }
    public string DisplayText { get; init; } = string.Empty;
}

public sealed partial class PurchaseDraftTargetSelectionModel : ObservableObject
{
    private bool _suppress;

    public string SourceType { get; init; } = string.Empty;
    public Guid SourceUid { get; init; }
    public string SourceNo { get; init; } = string.Empty;
    public Guid SupplierId { get; init; }
    public string SupplierName { get; init; } = string.Empty;
    public IReadOnlyList<PurchaseDraftTargetChoiceModel> Choices { get; init; } = [];

    [ObservableProperty]
    public partial PurchaseDraftTargetChoiceModel? SelectedChoice { get; set; }

    public event EventHandler? ValueChanged;

    public void SetInitialChoice(PurchaseDraftTargetChoiceModel choice)
    {
        _suppress = true;
        SelectedChoice = choice;
        _suppress = false;
    }

    partial void OnSelectedChoiceChanged(PurchaseDraftTargetChoiceModel? value)
    {
        if (!_suppress)
        {
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public PurchaseDraftTargetSelectionDto ToPreviewDto() => new()
    {
        SourceType = SourceType,
        SourceUid = SourceUid,
        SupplierId = SupplierId,
        TargetMode = SelectedChoice?.TargetMode ?? PurchaseDraftTargetModeEnum.CreateNew,
        ExistingPurchaseOrderId = SelectedChoice?.ExistingPurchaseOrderId,
        ExpectedPurchaseOrderVersion = SelectedChoice?.ExpectedPurchaseOrderVersion,
    };

    public CreatePurchaseDraftTargetCommand ToCreateDto() => new()
    {
        SupplierId = SupplierId,
        TargetMode = SelectedChoice?.TargetMode ?? PurchaseDraftTargetModeEnum.CreateNew,
        ExistingPurchaseOrderId = SelectedChoice?.ExistingPurchaseOrderId,
        ExpectedPurchaseOrderVersion = SelectedChoice?.ExpectedPurchaseOrderVersion,
    };
}

public sealed partial class PurchaseDraftWarningModel : ObservableObject
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public bool IsBlocker { get; init; }
    public bool RequiresConfirmation { get; init; }
    public string SourceType { get; init; } = string.Empty;
    public Guid? SourceUid { get; init; }

    [ObservableProperty]
    public partial bool IsConfirmed { get; set; }

    public event EventHandler? ConfirmationChanged;

    partial void OnIsConfirmedChanged(bool value) => ConfirmationChanged?.Invoke(this, EventArgs.Empty);

    public static PurchaseDraftWarningModel FromDto(PurchaseDraftWarningDto dto) => new()
    {
        Code = dto.Code ?? string.Empty,
        Message = dto.Message ?? string.Empty,
        IsBlocker = dto.IsBlocker,
        RequiresConfirmation = dto.RequiresConfirmation,
        SourceType = dto.SourceType ?? string.Empty,
        SourceUid = dto.SourceUid,
    };
}

public sealed class PurchaseDraftPreviewLineModel
{
    public string SourceNo { get; init; } = string.Empty;
    public string SupplierName { get; init; } = string.Empty;
    public string Sku { get; init; } = string.Empty;
    public string CustomerSku { get; init; } = string.Empty;
    public decimal PurchaseQty { get; init; }
    public DateTime? PlannedDeliveryDate { get; init; }
    public string DeliveryTargetSupplierName { get; init; } = string.Empty;
    public bool PurchaseDataIncomplete { get; init; }
    public int ContributionCount { get; init; }
}

public sealed class PurchaseDraftCreateResultModel
{
    public string SourceType { get; init; } = string.Empty;
    public Guid SourceUid { get; init; }
    public NetDemandCommandOutcomeEnum Outcome { get; init; }
    public string ErrorCode { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string PurchaseOrderNos { get; init; } = string.Empty;

    public static PurchaseDraftCreateResultModel FromDto(CreatePurchaseDraftSourceResult dto) => new()
    {
        SourceType = dto.SourceType ?? string.Empty,
        SourceUid = dto.SourceUid,
        Outcome = dto.Outcome,
        ErrorCode = dto.ErrorCode ?? string.Empty,
        Message = dto.Message ?? string.Empty,
        PurchaseOrderNos = string.Join(", ", dto.PurchaseDrafts.Select(item => item.OrderNo).Where(item => !string.IsNullOrWhiteSpace(item))),
    };
}
