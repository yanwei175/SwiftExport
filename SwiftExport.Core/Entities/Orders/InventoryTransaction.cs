using SwiftExport.Core.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace SwiftExport.Core.Entities.Orders;

/// <summary>
/// 库存交易流水实体（对应 dbo.InventoryTransaction）。
/// 记录每一笔库存变动的明细，用于对账、审计及历史库存追溯。
/// </summary>
[Table("InventoryTransaction")]
public class InventoryTransaction : BaseEntity
{
    /// <summary>
    /// 仓库编码 (例如：V_WH 代表供应商虚拟仓)
    /// </summary>
    public string WhseCode { get; set; } = string.Empty;

    /// <summary>
    /// 库位编码 (在虚拟仓模式下，通常存储供应商编码 SupplierCode)
    /// </summary>
    public string LocationCode { get; set; } = string.Empty;

    /// <summary>
    /// 物料编码 SKU
    /// </summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>
    /// 业务类型。可选值参考：
    /// (采购入库), 
    /// (销售出库), 
    /// (库存调整), 
    /// (采购退货)
    /// </summary>
    public string BizType { get; set; } = string.Empty;

    /// <summary>
    /// 业务单号 (如：入库单号 ReceiveNo 或 销售单号 SoNo)
    /// </summary>
    public string BizNo { get; set; } = string.Empty;

    /// <summary>
    /// 业务行 ID。关联入库单行或采购行 ID，用于精准定位变动来源。
    /// </summary>
    public int? BizLineId { get; set; }

    /// <summary>
    /// 进项数量 (仅入库业务为正，其他为 0)
    /// </summary>
    public decimal InQty { get; set; }

    /// <summary>
    /// 销项数量 (仅出库业务为正，其他为 0)
    /// </summary>
    public decimal OutQty { get; set; }

    /// <summary>
    /// 本次变动总量。入库为正数，出库为负数。
    /// 公式：ChangeQty = InQty - OutQty
    /// </summary>
    public decimal ChangeQty { get; set; }

    /// <summary>
    /// 变动后库存余量快照。
    /// 记录该笔业务执行完毕后，当前 SKU 在该库位的即时库存余额。
    /// </summary>
    public decimal PostQty { get; set; }

    /// <summary>
    /// 成本单价。记录入库时的采购单价或出库时的成本价，用于财务核算。
    /// </summary>
    public decimal? UnitPrice { get; set; }

    /// <summary>
    /// 变动总金额 (ChangeQty * UnitPrice)。
    /// </summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// 参考单号。用于多级追溯，如入库单流水可以记录原始采购单号 (PoNo)。
    /// </summary>
    public string? RefNo { get; set; }
}
