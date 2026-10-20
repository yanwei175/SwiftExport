using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>只读采购跟进行，包装专用查询快照并区分未知数量、部分备好和全部备好。</summary>
public sealed partial class PurchaseOrderProgressLineModel
{
    private readonly PurchaseOrderProgressLineDto _dto;

    private PurchaseOrderProgressLineModel(PurchaseOrderProgressLineDto dto) => _dto = dto;

    /// <summary>从跟进查询快照建立只读展示，不复制采购编辑状态。</summary>
    public static PurchaseOrderProgressLineModel FromDto(PurchaseOrderProgressLineDto dto) => new(dto);

    public Guid Uid => _dto.PurchaseOrderLineId;
    public int Version => _dto.Version;
    public int LineNumber => _dto.LineNumber;
    public string Sku => _dto.Sku;
    public string SupplierSku => _dto.SupplierSku;
    public string DescriptionZh => _dto.DescriptionZh;
    public string MainImage => _dto.MainImage;
    public decimal Quantity => _dto.Quantity;
    public DateTime? PlannedDeliveryDate => _dto.PlannedDeliveryDate;

    /// <summary>按序号降序展示报告形成后的完整快照。</summary>
    public IReadOnlyList<SupplierProgressReportDto> ProgressHistory => _dto.ProgressHistory.OrderByDescending(x => x.Sequence).ToArray();
    /// <summary>以最高序号判定最新报告。</summary>
    public SupplierProgressReportDto? LatestProgress => ProgressHistory.FirstOrDefault() ?? _dto.LatestProgress;
    public decimal? LatestReadyQty => LatestProgress?.ReadyQty;
    /// <summary>未知数量保持可见，不伪造为零。</summary>
    public string LatestReadyQtyText => LatestProgress is null ? "尚未跟进" : LatestReadyQty?.ToString("#,##0.####") ?? "尚未反馈";
    public string LatestExpectedReadyDateText => LatestProgress is null ? "尚未跟进" : LatestProgress.ExpectedReadyDate?.ToString("yyyy-MM-dd") ?? "尚未反馈";
    public string LatestFollowUpDateText => LatestProgress is null ? "尚未跟进" : LatestProgress.SupplierReportedDate.ToString("yyyy-MM-dd HH:mm");
    public string LatestFollowUpUserText => LatestProgress is null ? "尚未跟进" : LatestProgress.CreatedBy;
}
