using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExportClient.Features.DemandPlanning.GrossDemand.Models;

public sealed partial class GrossDemandRowModel : ObservableObject
{
    public Guid Uid { get; private set; }
    public int Version { get; private set; }
    public GrossDemandLifecycleStatusEnum LifecycleStatus { get; private set; }
    public GrossDemandDecompositionStatusEnum DecompositionStatus { get; private set; }
    public int DecompositionRevision { get; private set; }
    public string SourceType { get; private set; } = string.Empty;
    public Guid SourceUid { get; private set; }
    public string SourceNo { get; private set; } = string.Empty;
    public Guid SourceLineId { get; private set; }
    public int? SourceLineNumber { get; private set; }
    public Guid? ProductId { get; private set; }
    public bool IsNonStandard { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string LinkSku { get; private set; } = string.Empty;
    public string CustomerSku { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Color { get; private set; } = string.Empty;
    public string Specification { get; private set; } = string.Empty;
    public string ImagePath { get; private set; } = string.Empty;
    public decimal DemandQty { get; private set; }
    public DateTime? RequiredDate { get; private set; }
    public string Remark { get; private set; } = string.Empty;
    public int ActiveNetDemandCount { get; private set; }
    public int CustomerSuppliedNetDemandCount { get; private set; }
    public DemandExecutionProgressEnum ExecutionProgress { get; private set; }
    public bool CanDecompose { get; private set; }
    public bool CanRollback { get; private set; }
    public string RollbackBlockerSummary { get; private set; } = string.Empty;
    public DateTime? DecomposedAt { get; private set; }
    public string DecomposedBy { get; private set; } = string.Empty;
    public DateTime? CreatedDate { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTime? UpdatedDate { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;
    public string LifecycleStatusText => LifecycleStatus switch
    {
        GrossDemandLifecycleStatusEnum.Active => "活动",
        GrossDemandLifecycleStatusEnum.Closed => "已关闭",
        GrossDemandLifecycleStatusEnum.Voided => "已作废",
        _ => LifecycleStatus.ToString(),
    };
    public string DecompositionStatusText => DecompositionStatus == GrossDemandDecompositionStatusEnum.Decomposed ? "已拆解" : "待拆解";
    public string ExecutionProgressText => ExecutionProgress switch
    {
        DemandExecutionProgressEnum.NotStarted => "未开始",
        DemandExecutionProgressEnum.PartiallyAllocated => "部分分配",
        DemandExecutionProgressEnum.FullyAllocated => "全部分配",
        DemandExecutionProgressEnum.PartiallyCompleted => "部分完成",
        DemandExecutionProgressEnum.Completed => "已完成",
        _ => ExecutionProgress.ToString(),
    };
    public string SourceTypeText => SourceType switch
    {
        "SO" => "销售",
        "MD" => "手工",
        "CR" => "投诉",
        _ => SourceType,
    };
    public string ItemTypeText => IsNonStandard ? "非标" : "标准";

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    public static GrossDemandRowModel FromDto(GrossDemandListItemDto dto)
    {
        return new GrossDemandRowModel
        {
            Uid = dto.Uid,
            Version = dto.Version,
            LifecycleStatus = dto.LifecycleStatus,
            DecompositionStatus = dto.DecompositionStatus,
            DecompositionRevision = dto.DecompositionRevision,
            SourceType = dto.SourceType ?? string.Empty,
            SourceUid = dto.SourceUid,
            SourceNo = dto.SourceNo ?? string.Empty,
            SourceLineId = dto.SourceLineId,
            SourceLineNumber = dto.SourceLineNumber,
            ProductId = dto.ProductId,
            IsNonStandard = dto.IsNonStandard,
            Sku = dto.Sku ?? string.Empty,
            LinkSku = dto.LinkSku ?? string.Empty,
            CustomerSku = dto.CustomerSku ?? string.Empty,
            Description = dto.Description ?? string.Empty,
            Color = dto.Color ?? string.Empty,
            Specification = dto.Specification ?? string.Empty,
            ImagePath = dto.ImagePath ?? string.Empty,
            DemandQty = dto.DemandQty,
            RequiredDate = dto.RequiredDate,
            Remark = dto.Remark ?? string.Empty,
            ActiveNetDemandCount = dto.ActiveNetDemandCount,
            CustomerSuppliedNetDemandCount = dto.CustomerSuppliedNetDemandCount,
            ExecutionProgress = dto.ExecutionProgress,
            CanDecompose = dto.CanDecompose,
            CanRollback = dto.CanRollback,
            RollbackBlockerSummary = dto.RollbackBlockerSummary ?? string.Empty,
            DecomposedAt = dto.DecomposedAt,
            DecomposedBy = dto.DecomposedBy ?? string.Empty,
            CreatedDate = dto.CreatedDate,
            CreatedBy = dto.CreatedBy ?? string.Empty,
            UpdatedDate = dto.UpdatedDate,
            UpdatedBy = dto.UpdatedBy ?? string.Empty,
        };
    }
}
