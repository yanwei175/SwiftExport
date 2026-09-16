using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using SwiftExport.Core.DatabaseSchema;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Procurement;

/// <summary>
/// 采购订单头实体，是采购单生命周期、供应商关系、需求来源和商业快照的权威模型。
/// 对应物理表 dbo.PurchaseOrderHead。
/// </summary>
[Table("PurchaseOrderHead")]
public sealed partial class PurchaseOrderHead : BaseEntity
{
    /// <summary>来源类别：0 为历史未分类，1 为需求，2 为手工；不以来源缺失推断类别。</summary>
    public int OriginKind { get; set; }
    /// <summary>采购货物的客户归属；历史未知记录保持空值。</summary>
    public Guid? CustomerId { get; set; }
    /// <summary>创建时的客户名称，不随主数据修改。</summary>
    public string? CustomerNameSnapshot { get; set; }
    /// <summary>本订单当前生效的确认依据批次。</summary>
    public Guid? ActiveConfirmationId { get; set; }
    /// <summary>本切片手工录入的包装商务要求，不读取当前客户默认覆盖历史。</summary>
    public string? PackagingRequirements { get; set; }
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

    /// <summary>采购创建时采用的配置币种快照；仅用于同币种展示与记账，本模块不执行币种换算。</summary>
    public string Currency { get; set; } = "CNY";

    /// <summary>采购单采用的价格倍率快照；1 表示原价，小于 1 表示折扣，大于 1 表示加价，不在实体中换算或覆盖行价格。</summary>
    [DbColumn(StoreType = "decimal(9,6)")]
    public decimal? DiscountRate { get; set; }

    /// <summary>用户手工填写的整单优惠金额快照，不由价格倍率自动计算；未填写或历史未知时保持空值。</summary>
    [DbColumn(StoreType = "decimal(18,4)")]
    public decimal? DiscountAmount { get; set; }

    /// <summary>采购供应商权威 Uid。</summary>
    public Guid SupplierId { get; set; }

    /// <summary>采购供应商编码快照。</summary>
    public string SupplierCode { get; set; } = string.Empty;

    /// <summary>采购供应商名称快照。</summary>
    public string? SupplierName { get; set; }

    /// <summary>形成采购单的需求来源类型；独立采购没有需求来源时为空。</summary>
    public string? DemandSourceType { get; set; }

    /// <summary>形成采购单的来源单据 Uid。</summary>
    public Guid? DemandSourceUid { get; set; }

    /// <summary>形成采购单的来源单号快照；独立采购没有来源单据时为空。</summary>
    public string? DemandSourceNo { get; set; }

    /// <summary>创建请求 Uid；旧链用于需求转换，新链对应独立创建命令。</summary>
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
