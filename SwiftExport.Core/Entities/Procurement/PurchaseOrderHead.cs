using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Procurement;

/// <summary>
/// 采购订单头实体，是采购单生命周期、供应商关系、需求来源和商业快照的权威模型。
/// 对应物理表 dbo.PurchaseOrderHead。
/// </summary>
[Table("PurchaseOrderHead")]
public sealed class PurchaseOrderHead : BaseEntity
{
    /// <summary>采购确认状态；草稿为待确认，正式下单后为已确认。</summary>
    public ConfirmationStatusEnum ConfirmationStatus { get; set; } = ConfirmationStatusEnum.待确认;

    /// <summary>采购生命周期；业务判断不得再读取旧 OrderStatus。</summary>
    public PurchaseOrderLifecycleStatusEnum LifecycleStatus { get; set; } = PurchaseOrderLifecycleStatusEnum.Active;

    /// <summary>采购单号，是供人工查账的业务编码。</summary>
    public string OrderNo { get; set; } = string.Empty;

    /// <summary>诗亦霖内部采购单号快照。</summary>
    public string? BoNo { get; set; }

    /// <summary>客户采购单号快照。</summary>
    public string? CustomerPoNo { get; set; }

    /// <summary>客户代码快照。</summary>
    public string? CustomerCode { get; set; }

    /// <summary>币种代码；当前采购固定为人民币。</summary>
    public string Currency { get; set; } = "CNY";

    /// <summary>采购供应商权威 Uid。</summary>
    public Guid SupplierId { get; set; }

    /// <summary>采购供应商编码快照。</summary>
    public string SupplierCode { get; set; } = string.Empty;

    /// <summary>采购供应商名称快照。</summary>
    public string? SupplierName { get; set; }

    /// <summary>形成采购单的需求来源类型。</summary>
    public string DemandSourceType { get; set; } = string.Empty;

    /// <summary>形成采购单的来源单据 Uid。</summary>
    public Guid DemandSourceUid { get; set; }

    /// <summary>形成采购单的来源单号快照，可用于销售订单号检索。</summary>
    public string DemandSourceNo { get; set; } = string.Empty;

    /// <summary>净需求转采购请求 Uid，是采购头创建幂等范围的一部分。</summary>
    public Guid ExecutionRequestId { get; set; }

    /// <summary>业务员名称快照。</summary>
    public string? SoPerson { get; set; }

    /// <summary>采购或跟单员名称快照。</summary>
    public string? PoPerson { get; set; }

    /// <summary>进水管要求快照。</summary>
    public string? InletrHoseType { get; set; }

    /// <summary>起泡器要求快照。</summary>
    public string? AeratorType { get; set; }

    /// <summary>托盘要求快照。</summary>
    public string? PalletType { get; set; }

    /// <summary>内盒要求快照。</summary>
    public string? BoxType { get; set; }

    /// <summary>标贴要求快照。</summary>
    public string? LabelType { get; set; }

    /// <summary>说明书要求快照。</summary>
    public string? IMType { get; set; }

    /// <summary>外箱要求快照。</summary>
    public string? CartonType { get; set; }

    /// <summary>Logo 或标识要求快照。</summary>
    public string? LogoType { get; set; }

    /// <summary>复核人名称快照。</summary>
    public string? ReviewedBy { get; set; }

    /// <summary>采购文件夹路径或编号。</summary>
    public string? PoFolder { get; set; }

    /// <summary>折扣及特殊商务备注。</summary>
    public string? ZheKouBeiZhu { get; set; }

    /// <summary>采购单通用备注。</summary>
    public string? Remark { get; set; }

    /// <summary>采购单日期。</summary>
    public DateTime OrderDate { get; set; }

    /// <summary>采购头预计交付日期，取当前有效采购行的最早计划交期。</summary>
    public DateTime? ExpectedDate { get; set; }

    /// <summary>供应商回复交期。</summary>
    public DateTime? ReplyDate { get; set; }

    /// <summary>最近一次确认时间；完整历史由业务操作日志保存。</summary>
    public DateTime? ConfirmedDate { get; set; }

    /// <summary>最近一次确认人名称。</summary>
    public string? ConfirmedBy { get; set; }
}
