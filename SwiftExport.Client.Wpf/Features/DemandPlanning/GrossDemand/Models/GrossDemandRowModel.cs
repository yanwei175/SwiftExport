using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExportClient.Features.DemandPlanning.GrossDemand.Models;

/// <summary>承载来源需求本体快照、兼容拆解状态与页面选择状态。</summary>
public sealed partial class GrossDemandRowModel : ObservableObject
{
    public Guid Uid { get; private set; }
    public int Version { get; private set; }
    public GrossDemandLifecycleStatusEnum LifecycleStatus { get; private set; }
    public string SourceType { get; private set; } = string.Empty;
    public Guid SourceUid { get; private set; }
    public string SourceNo { get; private set; } = string.Empty;
    public Guid SourceLineId { get; private set; }
    public int? SourceLineNumber { get; private set; }
    public int? SourceVersion { get; private set; }
    public Guid? CustomerId { get; private set; }
    public Guid? ProductId { get; private set; }
    public bool IsNonStandard { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string LinkSku { get; private set; } = string.Empty;
    public string CustomerSku { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Color { get; private set; } = string.Empty;
    public string Specification { get; private set; } = string.Empty;
    public string ImagePath { get; private set; } = string.Empty;
    public string Unit { get; private set; } = string.Empty;
    public decimal DemandQty { get; private set; }
    public DateTime? RequiredDate { get; private set; }
    public string Remark { get; private set; } = string.Empty;
    public bool HasActiveMaterial { get; private set; }
    public bool HasValidPurchase { get; private set; }
    public bool HasPostedDelivery { get; private set; }
    /// <summary>独立存在性提示；不表示采购数量已覆盖需求或交付已完成。</summary>
    public string TraceSummary => $"拆解{(HasActiveMaterial ? "有" : "无")} / 采购{(HasValidPurchase ? "有" : "无")} / 交付{(HasPostedDelivery ? "有" : "无")}";
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
    public string SourceTypeText => SourceType switch
    {
        "SO" => "销售单",
        "MD" => "手工单",
        "CR" => "投诉单",
        _ => SourceType,
    };
    public string ItemTypeText => IsNonStandard ? "非标件" : "标准件";

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    public static GrossDemandRowModel FromDto(GrossDemandListItemDto dto)
    {
        return new GrossDemandRowModel
        {
            Uid = dto.Uid,
            Version = dto.Version,
            LifecycleStatus = dto.LifecycleStatus,
            SourceType = dto.SourceType ?? string.Empty,
            SourceUid = dto.SourceUid,
            SourceNo = dto.SourceNo ?? string.Empty,
            SourceLineId = dto.SourceLineId,
            SourceLineNumber = dto.SourceLineNumber,
            SourceVersion = dto.SourceVersion,
            CustomerId = dto.CustomerId,
            ProductId = dto.ProductId,
            IsNonStandard = dto.IsNonStandard,
            Sku = dto.Sku ?? string.Empty,
            LinkSku = dto.LinkSku ?? string.Empty,
            CustomerSku = dto.CustomerSku ?? string.Empty,
            Description = dto.Description ?? string.Empty,
            Color = dto.Color ?? string.Empty,
            Specification = dto.Specification ?? string.Empty,
            ImagePath = dto.ImagePath ?? string.Empty,
            Unit = dto.Unit ?? string.Empty,
            DemandQty = dto.DemandQty,
            RequiredDate = dto.RequiredDate,
            Remark = dto.Remark ?? string.Empty,
            HasActiveMaterial = dto.HasActiveMaterial,
            HasValidPurchase = dto.HasValidPurchase,
            HasPostedDelivery = dto.HasPostedDelivery,
            CreatedDate = dto.CreatedDate,
            CreatedBy = dto.CreatedBy ?? string.Empty,
            UpdatedDate = dto.UpdatedDate,
            UpdatedBy = dto.UpdatedBy ?? string.Empty,
        };
    }
}
