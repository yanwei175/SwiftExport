using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.Models;

/// <summary>表示物料需求工作台的一条只读物料事实及客户端选择状态。</summary>
public sealed partial class NetDemandRowModel : ObservableObject
{
    /// <summary>物料需求 Uid。</summary>
    public Guid Uid { get; init; }
    /// <summary>物料需求并发版本。</summary>
    public int Version { get; init; }
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
    /// <summary>当前净需求数量。</summary>
    public decimal NetQty { get; init; }
    /// <summary>来源需求交期。</summary>
    public DateTime? DemandRequiredDate { get; init; }
    /// <summary>兼容旧计划编辑器的 Allocation Uid。</summary>
    public Guid AllocationId { get; init; }
    /// <summary>兼容旧计划编辑器的 Allocation 并发版本。</summary>
    public int AllocationVersion { get; init; }
    /// <summary>兼容旧计划编辑器的执行方式。</summary>
    public DemandExecutionTypeEnum ExecutionType { get; init; }
    /// <summary>兼容旧计划编辑器的供应商分配来源。</summary>
    public SupplierAssignmentSourceEnum AssignmentSource { get; init; }
    /// <summary>兼容旧计划编辑器的计划数量。</summary>
    public decimal PlannedQty { get; init; }
    /// <summary>兼容旧采购预览的已关联数量。</summary>
    public decimal AllocatedQty { get; init; }
    /// <summary>兼容旧采购预览的剩余数量。</summary>
    public decimal RemainingQty { get; init; }
    /// <summary>兼容旧计划编辑器的计划交期。</summary>
    public DateTime? PlannedDeliveryDate { get; init; }
    /// <summary>兼容旧计划编辑器的采购供应商 Uid。</summary>
    public Guid? PurchaseSupplierId { get; init; }
    /// <summary>兼容旧计划编辑器的采购供应商编码。</summary>
    public string PurchaseSupplierCode { get; init; } = string.Empty;
    /// <summary>兼容旧计划编辑器的采购供应商名称。</summary>
    public string PurchaseSupplierName { get; init; } = string.Empty;
    /// <summary>兼容旧计划编辑器的送达供应商 Uid。</summary>
    public Guid? DeliveryTargetSupplierId { get; init; }
    /// <summary>兼容旧计划编辑器的送达供应商编码。</summary>
    public string DeliveryTargetSupplierCode { get; init; } = string.Empty;
    /// <summary>兼容旧计划编辑器的送达供应商名称。</summary>
    public string DeliveryTargetSupplierName { get; init; } = string.Empty;
    /// <summary>兼容旧计划编辑器的产品默认供应商名称。</summary>
    public string DefaultSupplierName { get; init; } = string.Empty;
    /// <summary>兼容旧计划编辑器的默认供应商变化标记。</summary>
    public bool DefaultSupplierChanged { get; init; }
    /// <summary>兼容旧计划与采购页面的汇总状态。</summary>
    public NetDemandPlanStatusEnum PlanStatus { get; init; }
    /// <summary>兼容旧页面的阻断摘要。</summary>
    public string BlockerSummary { get; init; } = string.Empty;
    /// <summary>兼容旧计划编辑器的替代准入。</summary>
    public bool CanReplacePlan { get; init; }
    /// <summary>兼容旧计划编辑器的默认供应商准入。</summary>
    public bool CanApplyDefaultSupplier { get; init; }
    /// <summary>有效采购 Link 对应的采购单号。</summary>
    public string PurchaseOrderNos { get; init; } = string.Empty;

    /// <summary>兼容旧计划页面的执行方式显示文本。</summary>
    public string ExecutionTypeText => ExecutionType switch
    {
        DemandExecutionTypeEnum.Purchase => "采购",
        DemandExecutionTypeEnum.Production => "生产",
        DemandExecutionTypeEnum.Outsourcing => "外协",
        _ => ExecutionType.ToString(),
    };

    /// <summary>兼容旧计划页面的计划状态显示文本。</summary>
    public string PlanStatusText => PlanStatus switch
    {
        NetDemandPlanStatusEnum.UnassignedSupplier => "待分配供应商",
        NetDemandPlanStatusEnum.DeliveryTargetPending => "送达目标待分配",
        NetDemandPlanStatusEnum.ReadyForPurchase => "可转采购",
        NetDemandPlanStatusEnum.PartiallyAllocated => "部分转采购",
        NetDemandPlanStatusEnum.FullyAllocated => "已转采购",
        NetDemandPlanStatusEnum.DownstreamDraft => "采购草稿",
        NetDemandPlanStatusEnum.DownstreamConfirmed => "采购已确认",
        NetDemandPlanStatusEnum.DataCorrupted => "数据异常",
        NetDemandPlanStatusEnum.ExecutionWorkspacePending => "执行工作台待接入",
        _ => PlanStatus.ToString(),
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

    /// <summary>兼容未迁移计划与采购消费者的旧工作台 DTO 映射。</summary>
    public static NetDemandRowModel FromDto(NetDemandListItemDto dto)
    {
        return new NetDemandRowModel
        {
            Uid = dto.Uid,
            Version = dto.Version,
            GrossDemandId = dto.GrossDemandId,
            DecompositionRevision = dto.DecompositionRevision,
            LineNumber = dto.LineNumber,
            SourceType = dto.SourceType ?? string.Empty,
            SourceUid = dto.SourceUid,
            SourceNo = dto.SourceNo ?? string.Empty,
            SourceLineNumber = dto.SourceLineNumber,
            RootNetDemandId = dto.RootNetDemandId,
            RootSku = dto.RootSku ?? string.Empty,
            IsCustomerSupplied = dto.IsCustomerSupplied,
            ProductId = dto.ProductId,
            IsNonStandard = dto.IsNonStandard,
            Sku = dto.Sku ?? string.Empty,
            LinkSku = dto.LinkSku ?? string.Empty,
            CustomerSku = dto.CustomerSku ?? string.Empty,
            Description = dto.Description ?? string.Empty,
            Color = dto.Color ?? string.Empty,
            Specification = dto.Specification ?? string.Empty,
            ImagePath = dto.ImagePath ?? string.Empty,
            NetQty = dto.NetQty,
            DemandRequiredDate = dto.DemandRequiredDate,
            AllocationId = dto.AllocationId,
            AllocationVersion = dto.AllocationVersion,
            ExecutionType = dto.ExecutionType,
            AssignmentSource = dto.AssignmentSource,
            PlannedQty = dto.PlannedQty,
            AllocatedQty = dto.AllocatedQty,
            RemainingQty = dto.RemainingQty,
            PlannedDeliveryDate = dto.PlannedDeliveryDate,
            PurchaseSupplierId = dto.PurchaseSupplierId,
            PurchaseSupplierCode = dto.PurchaseSupplierCode ?? string.Empty,
            PurchaseSupplierName = dto.PurchaseSupplierName ?? string.Empty,
            DeliveryTargetSupplierId = dto.DeliveryTargetSupplierId,
            DeliveryTargetSupplierCode = dto.DeliveryTargetSupplierCode ?? string.Empty,
            DeliveryTargetSupplierName = dto.DeliveryTargetSupplierName ?? string.Empty,
            DefaultSupplierName = dto.DefaultSupplierName ?? string.Empty,
            DefaultSupplierChanged = dto.DefaultSupplierChanged,
            PlanStatus = dto.PlanStatus,
            PurchaseOrderNos = dto.PurchaseOrderNos ?? string.Empty,
            BlockerSummary = dto.BlockerSummary ?? string.Empty,
            CanReplacePlan = dto.CanReplacePlan,
            CanApplyDefaultSupplier = dto.CanApplyDefaultSupplier,
        };
    }
}
