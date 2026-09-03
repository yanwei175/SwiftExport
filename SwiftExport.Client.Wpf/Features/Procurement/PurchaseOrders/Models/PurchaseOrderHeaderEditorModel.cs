using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;
using SwiftExport.Contracts.SysEnums;
using NewPurchaseOrderDetailDto = SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto.PurchaseOrderDetailDto;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>采购详情文档头模型；只允许草稿商业与包装字段产生修改。</summary>
public sealed partial class PurchaseOrderHeaderEditorModel : ObservableObject
{
    public Guid Uid { get; private set; }
    public int Version { get; set; }
    public string OrderNo { get; private set; } = string.Empty;
    public string SourceNo { get; private set; } = string.Empty;
    public string CustomerCode { get; private set; } = string.Empty;
    public string SupplierCode { get; private set; } = string.Empty;
    public string SupplierName { get; private set; } = string.Empty;
    public string Currency { get; private set; } = "CNY";
    public string CustomerName { get; private set; } = string.Empty;
    public string CustomerDisplay => string.Join(" ", new[] { CustomerCode, CustomerName }.Where(x => !string.IsNullOrWhiteSpace(x)));
    public string SupplierDisplay => string.Join(" ", new[] { SupplierCode, SupplierName }.Where(x => !string.IsNullOrWhiteSpace(x)));
    public bool IsLegacy { get; private set; }
    public ConfirmationStatusEnum ConfirmationStatus { get; set; }
    public PurchaseOrderLifecycleStatusEnum? LifecycleStatus { get; private set; }
    public DateTime? OrderDate { get; private set; }
    public DateTime? ExpectedDate { get; private set; }
    public DateTime? ConfirmedDate { get; set; }
    public string ConfirmedBy { get; set; } = string.Empty;
    public string ReviewedBy { get; private set; } = string.Empty;
    public string PurchaseOrderFolder { get; private set; } = string.Empty;
    public DateTime? CreatedDate { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTime? UpdatedDate { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;
    public int LineCount { get; private set; }
    public decimal TotalQty { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string OrderStatusText { get; private set; } = string.Empty;
    public string UnshippedQtyText { get; private set; } = "尚未接入";
    public string UsdTotalText { get; private set; } = "不适用（当前仅人民币）";
    public string CartonTotalText { get; private set; } = "尚未接入";
    public string CbmTotalText { get; private set; } = "尚未接入";
    public string InboundStatusText { get; private set; } = "尚未接入";
    public string ShippedStatusText { get; private set; } = "尚未接入";
    public bool CanEditDraft { get; set; }
    public bool CanConfirm { get; set; }
    public bool CanCancelConfirmation { get; set; }
    public bool CanReportProgress { get; set; }

    [ObservableProperty] public partial string BoNo { get; set; } = string.Empty;
    [ObservableProperty] public partial string CustomerPoNo { get; set; } = string.Empty;
    [ObservableProperty] public partial string SalesPerson { get; set; } = string.Empty;
    [ObservableProperty] public partial string PurchasePerson { get; set; } = string.Empty;
    [ObservableProperty] public partial DateTime? ReplyDate { get; set; }
    [ObservableProperty] public partial string InletHoseType { get; set; } = string.Empty;
    [ObservableProperty] public partial string AeratorType { get; set; } = string.Empty;
    [ObservableProperty] public partial string PalletType { get; set; } = string.Empty;
    [ObservableProperty] public partial string BoxType { get; set; } = string.Empty;
    [ObservableProperty] public partial string LabelType { get; set; } = string.Empty;
    [ObservableProperty] public partial string InstructionManualType { get; set; } = string.Empty;
    [ObservableProperty] public partial string CartonType { get; set; } = string.Empty;
    [ObservableProperty] public partial string LogoType { get; set; } = string.Empty;
    [ObservableProperty] public partial string DiscountRemark { get; set; } = string.Empty;
    [ObservableProperty] public partial string Remark { get; set; } = string.Empty;
    [ObservableProperty] public partial string PackagingRequirements { get; set; } = string.Empty;

    public void Load(PurchaseOrderDetailDto dto)
    {
        IsLegacy = true;
        var head = dto.Head;
        Uid = head.Uid; Version = head.Version; OrderNo = head.OrderNo ?? string.Empty;
        SourceNo = head.DemandSourceNo ?? dto.SalesOrderNo ?? string.Empty; CustomerCode = dto.CustomerCode ?? string.Empty;
        SupplierCode = head.SupplierCode ?? string.Empty; SupplierName = head.SupplierName ?? string.Empty;
        Currency = dto.CurrencyText ?? "CNY"; ConfirmationStatus = head.ConfirmationStatus; LifecycleStatus = head.LifecycleStatus;
        OrderDate = head.OrderDate; ExpectedDate = head.ExpectedDate; ConfirmedDate = dto.ConfirmedDate; ConfirmedBy = dto.ConfirmedBy ?? string.Empty;
        ReviewedBy = dto.ReviewedBy ?? string.Empty; PurchaseOrderFolder = dto.PurchaseOrderFolder ?? string.Empty;
        CreatedDate = dto.CreatedDate; CreatedBy = dto.CreatedBy ?? string.Empty; UpdatedDate = dto.UpdatedDate; UpdatedBy = dto.UpdatedBy ?? string.Empty;
        LineCount = head.LineCount; TotalQty = head.TotalQty; TotalAmount = head.TotalAmount; OrderStatusText = head.OrderStatusText ?? string.Empty;
        UnshippedQtyText = head.UnshippedQtyText ?? "尚未接入"; UsdTotalText = dto.UsdTotalText ?? "不适用（当前仅人民币）";
        CartonTotalText = dto.CartonTotalText ?? "尚未接入"; CbmTotalText = dto.CbmTotalText ?? "尚未接入";
        InboundStatusText = dto.InboundStatusText ?? "尚未接入"; ShippedStatusText = dto.ShippedStatusText ?? "尚未接入";
        CanEditDraft = head.CanEditDraft; CanConfirm = head.CanConfirm; CanCancelConfirmation = head.CanCancelConfirmation; CanReportProgress = head.CanReportProgress;
        BoNo = head.BoNo ?? string.Empty; CustomerPoNo = dto.CustomerPoNo ?? string.Empty; SalesPerson = dto.SalesPerson ?? string.Empty;
        PurchasePerson = dto.PurchasePerson ?? string.Empty; ReplyDate = head.ReplyDate; InletHoseType = dto.InletHoseType ?? string.Empty;
        AeratorType = dto.AeratorType ?? string.Empty; PalletType = dto.PalletType ?? string.Empty; BoxType = dto.BoxType ?? string.Empty;
        LabelType = dto.LabelType ?? string.Empty; InstructionManualType = dto.InstructionManualType ?? string.Empty;
        CartonType = dto.CartonType ?? string.Empty; LogoType = dto.LogoType ?? string.Empty; DiscountRemark = dto.DiscountRemark ?? string.Empty;
        Remark = dto.Remark ?? string.Empty;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>初始化手工创建头部的可编辑基线。</summary>
    public void LoadCreate()
    {
        IsLegacy = false;
        CanEditDraft = true;
        CanConfirm = false;
        CanCancelConfirmation = false;
        OrderDate = DateTime.Today;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>将详情专用契约装入头模型，能力状态完全采用服务端返回值。</summary>
    public void LoadNew(NewPurchaseOrderDetailDto dto)
    {
        IsLegacy = false; Uid = dto.PurchaseOrderId; Version = dto.Version; OrderNo = dto.OrderNo ?? string.Empty;
        SourceNo = dto.DemandSourceNo ?? string.Empty; CustomerCode = dto.CustomerCode ?? string.Empty; CustomerName = dto.CustomerName ?? string.Empty;
        SupplierCode = dto.SupplierCode ?? string.Empty; SupplierName = dto.SupplierName ?? string.Empty; Currency = dto.Currency ?? "CNY";
        ConfirmationStatus = dto.ConfirmationStatus; LifecycleStatus = dto.LifecycleStatus; OrderDate = dto.OrderDate;
        ExpectedDate = dto.ExpectedDate; ConfirmedDate = dto.ConfirmedDate; ConfirmedBy = dto.ConfirmedBy ?? string.Empty;
        ReviewedBy = dto.ReviewedBy ?? string.Empty; PurchaseOrderFolder = dto.PurchaseOrderFolder ?? string.Empty;
        CreatedDate = dto.CreatedDate; UpdatedDate = dto.UpdatedDate; CreatedBy = dto.CreatedBy ?? string.Empty; UpdatedBy = dto.UpdatedBy ?? string.Empty;
        LineCount = dto.LineCount; TotalQty = dto.TotalQuantity; TotalAmount = dto.CnyTotalAmount;
        OrderStatusText = $"{dto.ConfirmationStatus} / {dto.LifecycleStatus}";
        CanEditDraft = dto.CanEditDraft; CanConfirm = dto.CanConfirm;
        CanCancelConfirmation = dto.CanCancelConfirmation; CanReportProgress = false;
        BoNo = dto.BoNo ?? string.Empty; CustomerPoNo = dto.CustomerPoNo ?? string.Empty; SalesPerson = dto.SalesPerson ?? string.Empty;
        PurchasePerson = dto.PurchasePerson ?? string.Empty; InletHoseType = dto.InletHoseType ?? string.Empty;
        AeratorType = dto.AeratorType ?? string.Empty; PalletType = dto.PalletType ?? string.Empty; BoxType = dto.BoxType ?? string.Empty;
        LabelType = dto.LabelType ?? string.Empty; InstructionManualType = dto.InstructionManualType ?? string.Empty;
        CartonType = dto.CartonType ?? string.Empty; LogoType = dto.LogoType ?? string.Empty; DiscountRemark = dto.DiscountRemark ?? string.Empty;
        ReplyDate = dto.ReplyDate; Remark = dto.Remark ?? string.Empty; PackagingRequirements = dto.PackagingRequirements ?? string.Empty;
        OnPropertyChanged(string.Empty);
    }
}
