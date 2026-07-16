using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.Models;

public sealed partial class NetDemandRowModel : ObservableObject
{
    public Guid Uid { get; private set; }
    public int Version { get; private set; }
    public Guid GrossDemandId { get; private set; }
    public int DecompositionRevision { get; private set; }
    public int LineNumber { get; private set; }
    public string SourceType { get; private set; } = string.Empty;
    public Guid SourceUid { get; private set; }
    public string SourceNo { get; private set; } = string.Empty;
    public int? SourceLineNumber { get; private set; }
    public Guid RootNetDemandId { get; private set; }
    public string RootSku { get; private set; } = string.Empty;
    public bool IsCustomerSupplied { get; private set; }
    public Guid? ProductId { get; private set; }
    public bool IsNonStandard { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string LinkSku { get; private set; } = string.Empty;
    public string CustomerSku { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Color { get; private set; } = string.Empty;
    public string Specification { get; private set; } = string.Empty;
    public string ImagePath { get; private set; } = string.Empty;
    public decimal NetQty { get; private set; }
    public DateTime? DemandRequiredDate { get; private set; }
    public Guid AllocationId { get; private set; }
    public int AllocationVersion { get; private set; }
    public DemandExecutionTypeEnum ExecutionType { get; private set; }
    public SupplierAssignmentSourceEnum AssignmentSource { get; private set; }
    public decimal PlannedQty { get; private set; }
    public decimal AllocatedQty { get; private set; }
    public decimal RemainingQty { get; private set; }
    public DateTime? PlannedDeliveryDate { get; private set; }
    public Guid? PurchaseSupplierId { get; private set; }
    public string PurchaseSupplierCode { get; private set; } = string.Empty;
    public string PurchaseSupplierName { get; private set; } = string.Empty;
    public Guid? DeliveryTargetSupplierId { get; private set; }
    public string DeliveryTargetSupplierCode { get; private set; } = string.Empty;
    public string DeliveryTargetSupplierName { get; private set; } = string.Empty;
    public string DefaultSupplierName { get; private set; } = string.Empty;
    public bool DefaultSupplierChanged { get; private set; }
    public NetDemandPlanStatusEnum PlanStatus { get; private set; }
    public string PurchaseOrderNos { get; private set; } = string.Empty;
    public string BlockerSummary { get; private set; } = string.Empty;
    public bool CanReplacePlan { get; private set; }
    public bool CanApplyDefaultSupplier { get; private set; }

    public string SourceTypeText => SourceType switch
    {
        "SO" => "销售订单",
        "MD" => "手工需求",
        "CR" => "投诉补货",
        _ => SourceType,
    };

    public string DemandRoleText => IsCustomerSupplied ? "客供件" : "父件";

    public string DisplaySku => IsCustomerSupplied ? $"    ↳ {Sku}" : Sku;

    public string ExecutionTypeText => ExecutionType switch
    {
        DemandExecutionTypeEnum.Purchase => "采购",
        DemandExecutionTypeEnum.Production => "生产",
        DemandExecutionTypeEnum.Outsourcing => "外协",
        _ => ExecutionType.ToString(),
    };

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

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

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
