using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using NewPurchaseOrderLineDto = SwiftExport.Contracts.DTOs.Procurement.OrderManagement.ProcurementOrderLineDto;
using NewProgressSummaryDto = SwiftExport.Contracts.DTOs.Procurement.OrderManagement.ProcurementOrderProgressSummaryDto;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>
/// 采购行编辑与跟单展示模型，保留采购业务字段的可编辑状态，并将最新供应商反馈投影为明确的只读文本。
/// </summary>
public sealed partial class PurchaseOrderLineEditorModel : ObservableObject
{
    public Guid Uid { get; init; }
    public int Version { get; init; }
    public int LineNumber { get; init; }
    public string Sku { get; init; } = string.Empty;
    public string CustomerSku { get; init; } = string.Empty;
    public string LinkSku { get; init; } = string.Empty;
    public string FinishZh { get; init; } = string.Empty;
    public string FeaturesZh { get; init; } = string.Empty;
    public string MaterialType { get; init; } = string.Empty;
    public string SupplyType { get; init; } = string.Empty;
    public string MainImage { get; init; } = string.Empty;
    public string UnitSnapshot { get; init; } = string.Empty;
    public string WarehouseName { get; init; } = string.Empty;
    public string SourceType { get; init; } = string.Empty;
    public string SourceNo { get; init; } = string.Empty;
    public int? SourceLineNumber { get; init; }
    public NewProgressSummaryDto? NewProgressSummary { get; init; }
    public decimal Quantity { get; init; }
    public decimal RecQty { get; init; }
    public decimal? UnitAmount { get; init; }
    public decimal? TaxAmount { get; init; }
    /// <summary>依据后台返回的采购数量和已过账入库数量计算未收货数量，显示时不强制补齐无意义的小数位。</summary>
    public string UnreceivedQtyText => $"{Math.Max(Quantity - RecQty, 0m):#,##0.####} / {Quantity:#,##0.####}";
    public decimal LinkedQty { get; init; }
    public string DeliverySupplierName { get; init; } = string.Empty;
    public IReadOnlyList<SupplierProgressReportDto> ProgressHistory { get; init; } = [];

    /// <summary>按最高 Sequence 选择最新报告；即使调用方顺序异常也不以集合首项冒充最新反馈。</summary>
    public SupplierProgressReportDto? LatestProgress => ProgressHistory.OrderByDescending(x => x.Sequence).FirstOrDefault();

    /// <summary>是否已有任何供应商跟进反馈。</summary>
    public bool HasProgress => LatestProgress is not null;

    /// <summary>最新安排状态文本；无反馈时明确显示“尚未跟进”。</summary>
    public string LatestArrangementText => NewProgressSummary is not null
        ? NewProgressSummary.ArrangementStatus.ToString()
        : LatestProgress is null ? "尚未跟进"
        : LatestProgress.ArrangementStatus switch
        {
            SwiftExport.Contracts.SysEnums.SupplierArrangementStatusEnum.NotArranged => "未安排",
            SwiftExport.Contracts.SysEnums.SupplierArrangementStatusEnum.Arranged => "已安排",
            SwiftExport.Contracts.SysEnums.SupplierArrangementStatusEnum.NotApplicable => "不适用",
            _ => "未知"
        };

    public decimal LatestReadyQty => LatestProgress?.ReadyQty ?? 0m;

    /// <summary>最新累计已备好数量文本；无反馈时不伪造为业务零。</summary>
    public string LatestReadyQtyText => NewProgressSummary is not null ? NewProgressSummary.ReadyQty.ToString("#,##0.####") : LatestProgress is null ? "尚未跟进" : LatestProgress.ReadyQty.ToString("#,##0.####");

    public DateTime? LatestExpectedReadyDate => LatestProgress?.ExpectedReadyDate;

    /// <summary>最新预计备妥日期文本。</summary>
    public string LatestExpectedReadyDateText => NewProgressSummary is not null
        ? NewProgressSummary.ExpectedReadyDate?.ToString("yyyy-MM-dd") ?? "未提供"
        : LatestProgress is null
        ? "尚未跟进"
        : LatestProgress.ExpectedReadyDate?.ToString("yyyy-MM-dd") ?? "未提供";

    /// <summary>后台生成的最近跟进时间文本。</summary>
    public string LatestFollowUpDateText => NewProgressSummary is not null
        ? NewProgressSummary.ReportedDate.ToString("yyyy-MM-dd HH:mm")
        : LatestProgress is null
        ? "尚未跟进"
        : LatestProgress.SupplierReportedDate.ToString("yyyy-MM-dd HH:mm");

    /// <summary>后台记录的最近跟单员文本。</summary>
    public string LatestFollowUpUserText => NewProgressSummary is not null
        ? string.IsNullOrWhiteSpace(NewProgressSummary.ReportedBy) ? "未知" : NewProgressSummary.ReportedBy
        : LatestProgress is null
        ? "尚未跟进"
        : string.IsNullOrWhiteSpace(LatestProgress.CreatedBy) ? "未知" : LatestProgress.CreatedBy;
    [ObservableProperty] public partial DateTime? PlannedDeliveryDate { get; set; }
    [ObservableProperty] public partial string SupplierSku { get; set; } = string.Empty;
    [ObservableProperty] public partial string DescriptionZh { get; set; } = string.Empty;
    [ObservableProperty] public partial decimal? UnitPrice { get; set; }
    [ObservableProperty] public partial decimal? TaxPrice { get; set; }
    [ObservableProperty] public partial string SubNote { get; set; } = string.Empty;

    /// <summary>从跟进详情契约建立前端行模型，保留后台返回的 Sequence 降序反馈历史。</summary>
    public static PurchaseOrderLineEditorModel FromDto(PurchaseOrderProgressLineDto dto) => new()
    {
        Uid = dto.PurchaseOrderLineId,
        Version = dto.Version,
        LineNumber = dto.LineNumber,
        Sku = dto.Sku ?? string.Empty,
        MainImage = dto.MainImage ?? string.Empty,
        Quantity = dto.Quantity,
        RecQty = dto.ReceivedQty,
        PlannedDeliveryDate = dto.PlannedDeliveryDate,
        SupplierSku = dto.SupplierSku ?? string.Empty,
        DescriptionZh = dto.DescriptionZh ?? string.Empty,
        ProgressHistory = dto.ProgressHistory,
    };

    /// <summary>将新链采购行映射到完整页面行模型；来源、进度、图片和包装属性保持空集合或空值。</summary>
    public static PurchaseOrderLineEditorModel FromNewDto(NewPurchaseOrderLineDto dto) => new()
    {
        Uid = dto.PurchaseOrderLineId, Version = dto.Version, LineNumber = dto.LineNumber, Sku = dto.Sku ?? string.Empty,
        CustomerSku = dto.CustomerSku ?? string.Empty, LinkSku = dto.LinkSku ?? string.Empty,
        FinishZh = dto.FinishZh ?? string.Empty, FeaturesZh = dto.FeaturesZh ?? string.Empty,
        MaterialType = dto.MaterialType ?? string.Empty, SupplyType = dto.SupplyType ?? string.Empty,
        UnitSnapshot = dto.UnitSnapshot ?? string.Empty, WarehouseName = dto.WarehouseName ?? string.Empty,
        SourceType = dto.SourceType ?? string.Empty, SourceNo = dto.SourceNo ?? string.Empty, SourceLineNumber = dto.SourceLineNumber,
        Quantity = dto.Quantity, UnitAmount = dto.UnitAmount, TaxAmount = dto.TaxAmount,
        DeliverySupplierName = dto.DeliverySupplierName ?? string.Empty, PlannedDeliveryDate = dto.PlannedDeliveryDate,
        SupplierSku = dto.SupplierSku ?? string.Empty, DescriptionZh = dto.DescriptionZh ?? string.Empty,
        UnitPrice = dto.UnitPrice, TaxPrice = dto.TaxPrice, SubNote = dto.SubNote ?? string.Empty,
        NewProgressSummary = dto.LatestProgress, ProgressHistory = []
    };
}
