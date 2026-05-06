using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Orders;

/// <summary>
/// 拆解独立需求之后的依赖需求实体
/// 用于承接“销售订单行 -> 采购/生产执行单”的拆解与跟踪数据。
/// </summary>
public class DependentDemand : BaseEntity
{
    #region 来源信息 (Source)

    /// <summary>
    /// 来源单据类型（枚举：SalesOrder, SampleOrder, Forecast, StockPlan 等）
    /// </summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>
    /// 来源单号（SO号、样品单号等）
    /// </summary>
    public string SourceNo { get; set; } = string.Empty;

    /// <summary>
    /// 来源单据行 ID
    /// </summary>
    public long SourceLineId { get; set; }

    /// <summary>
    /// 关联的独立需求 ID (毛需求 ID)
    /// </summary>
    public long IndependentDemandId { get; set; }

    #endregion


    #region 物料与层级 (Material & BOM)

    /// <summary>
    /// 当前物料 SKU
    /// </summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>
    /// 物料描述（用于非标件或补充说明）。
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 颜色名称（用于非标件或颜色补充）。
    /// </summary>
    public string? ColorName { get; set; }

    /// <summary>
    /// 规格特征（用于非标件或型号补充）。
    /// </summary>
    public string? Features { get; set; }

    /// <summary>
    /// 父项 SKU（所属成品）
    /// </summary>
    public string ParentSku { get; set; } = string.Empty;

    /// <summary>
    /// BOM 层级（0-成品/需求头, 1-一级子件, 2-二级...）
    /// </summary>
    public int BomLevel { get; set; }

    /// <summary>
    /// 供货类型（枚举：Purchase-采购, Produce-生产, Outsourcing-委外, Stock-库存）
    /// </summary>
    public string SupplyType { get; set; } = string.Empty;

    #endregion

    #region 数量跟踪 (Quantity Tracking)
    /// <summary>
    /// 毛需求数量 (原始需要的量)
    /// </summary>
    public decimal GrossQty { get; set; }

    /// <summary>
    /// 净需求数量 (考虑库存/在途后的实际建议量)
    /// </summary>
    public decimal NetQty { get; set; }

    /// <summary>
    /// 已转化执行的数量（已生成 PO 的数量或已转生产的数量）
    /// </summary>
    public decimal ExecutedQty { get; set; }

    /// <summary>
    /// 已入库/完工数量
    /// </summary>
    public decimal ReceivedQty { get; set; }

    /// <summary>
    /// 最终送货至（具体地址或仓库编码）
    /// 贸易场景下：可能直发客户，也可能入库
    /// </summary>
    public string? ShipToLocation { get; set; } = string.Empty;
    #endregion

    #region 执行状态 (Execution)

    /// <summary>
    /// 需求日期（什么时候要用）
    /// </summary>
    public DateTime? RequiredDate { get; set; }

    /// <summary>
    /// 状态（RequirementStatusEnums：采购中/已到货/已完成）
    /// </summary>
    public int StatusFlag { get; set; }


    /// <summary>
    /// 备注
    /// </summary>
    public string? Remark { get; set; }

    #endregion

    #region 执行关联 (Execution Link)

    /// <summary>
    /// 目标执行单号（如采购单号 PO-2024001）
    /// 理由：背靠背模式下，方便直接从需求池跳到采购单。
    /// </summary>
    public string? TargetNo { get; set; }

    /// <summary>
    /// 手工指定供应商编码（优先于产品默认供应商）。
    /// </summary>
    public string? SupplierCode { get; set; }

    /// <summary>
    /// 手工指定供应商名称。
    /// </summary>
    public string? SupplierName { get; set; }

    /// <summary>
    /// 目标执行行 ID
    /// </summary>
    public long? TargetLineId { get; set; }

    #endregion


    /// <summary>
    /// 业务变更时间（用于外部系统增量同步）。
    /// </summary>
    public DateTime? ChangeTime { get; set; }
}
