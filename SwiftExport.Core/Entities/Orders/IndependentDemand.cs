using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Orders;

/// <summary>
/// 销售需求执行实体 - 对应数据库 IndependentDemand 表
/// </summary>
public class IndependentDemand : BaseEntity
{
    // BaseEntity 包含：Id, CreatedDate, CreatedBy, UpdatedDate, UpdatedBy, IsDeleted, Status, Even
    #region 来源信息 (Source)

    /// <summary>
    /// 来源单据类型（枚举：SalesOrder, SampleOrder, Forecast, StockPlan 等）
    /// </summary>
    public required string SourceType { get; set; } 

    /// <summary>
    /// 来源单号（SO号、样品单号等）
    /// </summary>
    public required string SourceNo { get; set; }

    /// <summary>来源单据行 ID</summary>
    public int SourceLineId { get; set; }
    public required bool IsNonStandard { get; set; } 

    #endregion

    public required string Sku { get; set; } 
    public string? SourceSku { get; set; }//非标件需要有一个来源的sku,比如客户的型号
    public string? Description { get; set; } // 非标件描述或产品名称
    public string? ColorName { get; set; }   // 颜色名称
    public string? Features { get; set; }   // 规格型号（针对非标件尤为重要）

    public decimal OriginalQty { get; set; }  // 原始订单数量[cite: 2]
    public decimal FilledQty { get; set; }    // 已填充数量（已匹配采购或库存的数量）
    public decimal ShippedQty { get; set; }   // 已发货数量[cite: 1, 2]
    public bool IsKitted { get; set; }        // 是否齐套[cite: 1]
    public int OrderStatus { get; set; } = IndependentDemandStatusEnum.草稿;     // 内部执行状态 (0:待处理, 1:采购中, 2:已到货, 3:已完成)
    public DateTime? HopefulDate { get; set; }    // 期望交期
    public DateTime? LastShippedDate { get; set; } // 最后发货日期[cite: 2]
    public DateTime? ChangeTime { get; set; }      // 增量同步时间[cite: 1, 2]
    public string? Remark { get; set; }           // 备注[cite: 1]

    public int ApprovalStatus { get; set; }

    /// <summary>
    /// 是否已执行拆解（防重复拆解标记）。
    /// </summary>
    public bool IsDecomposed { get; set; }

    /// <summary>
    /// 最近一次拆解时间。
    /// </summary>
    public DateTime? DecomposedTime { get; set; }

}
