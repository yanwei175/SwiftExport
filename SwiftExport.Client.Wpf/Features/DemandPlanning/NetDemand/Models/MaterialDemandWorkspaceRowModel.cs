using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.Models;

/// <summary>表示物料需求工作台的一条只读物料事实及客户端选择状态。</summary>
public sealed partial class MaterialDemandWorkspaceRowModel : ObservableObject
{
    /// <summary>物料需求 Uid。</summary>
    public Guid Uid { get; init; }
    /// <summary>物料需求并发版本。</summary>
    public int Version { get; init; }
    /// <summary>物料需求生命周期。</summary>
    public NetDemandLifecycleStatusEnum LifecycleStatus { get; init; }
    /// <summary>所属来源需求 Uid。</summary>
    public Guid GrossDemandId { get; init; }
    /// <summary>来源需求拆解修订号。</summary>
    public int DecompositionRevision { get; init; }
    /// <summary>拆解修订内行号。</summary>
    public int LineNumber { get; init; }
    /// <summary>来源业务类型。</summary>
    public string SourceType { get; init; } = string.Empty;
    /// <summary>来源业务单据 Uid。</summary>
    public Guid SourceUid { get; init; }
    /// <summary>来源业务单号。</summary>
    public string SourceNo { get; init; } = string.Empty;
    /// <summary>来源业务行号。</summary>
    public int? SourceLineNumber { get; init; }
    /// <summary>拆解树根物料 Uid。</summary>
    public Guid RootNetDemandId { get; init; }
    /// <summary>拆解树根 SKU。</summary>
    public string RootSku { get; init; } = string.Empty;
    /// <summary>是否为客供件。</summary>
    public bool IsCustomerSupplied { get; init; }
    /// <summary>关联产品 Uid。</summary>
    public Guid? ProductId { get; init; }
    /// <summary>是否为非标物料。</summary>
    public bool IsNonStandard { get; init; }
    /// <summary>物料 SKU。</summary>
    public string Sku { get; init; } = string.Empty;
    /// <summary>产品关联 SKU。</summary>
    public string LinkSku { get; init; } = string.Empty;
    /// <summary>客户型号。</summary>
    public string CustomerSku { get; init; } = string.Empty;
    /// <summary>物料描述。</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>颜色。</summary>
    public string Color { get; init; } = string.Empty;
    /// <summary>规格。</summary>
    public string Specification { get; init; } = string.Empty;
    /// <summary>图片路径。</summary>
    public string ImagePath { get; init; } = string.Empty;
    /// <summary>物料类型。</summary>
    public string MaterialType { get; init; } = string.Empty;
    /// <summary>计量单位。</summary>
    public string Unit { get; init; } = string.Empty;
    /// <summary>拆解时供货方式快照。</summary>
    public string SupplyType { get; init; } = string.Empty;
    /// <summary>原始需求数量。</summary>
    public decimal GrossQty { get; init; }
    /// <summary>当前净需求数量。</summary>
    public decimal NetQty { get; init; }
    /// <summary>来源需求交期。</summary>
    public DateTime? DemandRequiredDate { get; init; }
    /// <summary>有效采购 Link 的净关联数量。</summary>
    public decimal AssociatedPurchaseQty { get; init; }
    /// <summary>有效采购 Link 对应的采购单号。</summary>
    public string PurchaseOrderNos { get; init; } = string.Empty;

    /// <summary>物料生命周期显示文本。</summary>
    public string LifecycleStatusText => LifecycleStatus switch
    {
        NetDemandLifecycleStatusEnum.Active => "活动",
        NetDemandLifecycleStatusEnum.RolledBack => "已回退",
        NetDemandLifecycleStatusEnum.Voided => "已作废",
        _ => LifecycleStatus.ToString(),
    };

    /// <summary>来源类型显示文本。</summary>
    public string SourceTypeText => SourceType switch
    {
        "SO" => "销售订单",
        "MD" => "手工需求",
        "CR" => "投诉补货",
        _ => SourceType,
    };

    /// <summary>父件或客供件显示文本。</summary>
    public string DemandRoleText => IsCustomerSupplied ? "客供件" : "父件";

    /// <summary>带层级缩进的 SKU。</summary>
    public string DisplaySku => IsCustomerSupplied ? $"    ↳ {Sku}" : Sku;

    /// <summary>客户端当前是否勾选该行。</summary>
    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    /// <summary>把专用只读 DTO 转换为包含客户端选择状态的行模型。</summary>
    public static MaterialDemandWorkspaceRowModel FromDto(MaterialDemandWorkspaceItemDto dto)
        => dto.Adapt<MaterialDemandWorkspaceRowModel>();
}
