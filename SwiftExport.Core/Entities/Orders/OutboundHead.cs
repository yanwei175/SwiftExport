using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Orders;

/// <summary>
/// 出库单头实体，正式承接手工出库和系统自动出库。
/// </summary>
[Table("OutboundHead")]
public class OutboundHead : BaseEntity
{
    /// <summary>
    /// 出库单号。
    /// </summary>
    public required string OutboundNo { get; set; }

    /// <summary>
    /// 出库日期。
    /// </summary>
    public DateTime? OutboundDate { get; set; }

    /// <summary>
    /// 业务类型，例如销售出库、客供件扣减、库存调整。
    /// </summary>
    public string? BizType { get; set; }

    /// <summary>
    /// 来源类型。
    /// </summary>
    public string? SourceType { get; set; }

    /// <summary>
    /// 来源单号。
    /// </summary>
    public string? SourceNo { get; set; }

    /// <summary>
    /// 出库仓库编码。
    /// </summary>
    public string? WarehouseCode { get; set; }

    /// <summary>
    /// 出库库位编码。
    /// </summary>
    public string? LocationCode { get; set; }

    /// <summary>
    /// 经办人。
    /// </summary>
    public string? Handler { get; set; }

    /// <summary>
    /// 是否已过账。
    /// </summary>
    public bool IsPosted { get; set; }

    /// <summary>
    /// 过账时间。
    /// </summary>
    public DateTime? PostedDate { get; set; }

    /// <summary>
    /// 过账人。
    /// </summary>
    public string? PostedBy { get; set; }

    /// <summary>
    /// 备注。
    /// </summary>
    public string? Remark { get; set; }
}
