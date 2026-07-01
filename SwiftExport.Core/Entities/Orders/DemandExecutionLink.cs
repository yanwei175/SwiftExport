using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Orders;

/// <summary>
/// 净需求执行关系实体，用于承接净需求到采购、生产、外协等执行单据的关系。
/// </summary>
[Table("DemandExecutionLink")]
public class DemandExecutionLink : BaseEntity
{
    /// <summary>
    /// 净需求行 ID。
    /// </summary>
    public required Guid DependentDemandId { get; set; }

    /// <summary>
    /// 目标执行类型，例如 PurchaseOrder、ManufacturingOrder、OutsourcingOrder。
    /// </summary>
    public required string TargetType { get; set; }

    /// <summary>
    /// 目标执行单头 ID。
    /// </summary>
    public Guid? TargetHeadId { get; set; }

    /// <summary>
    /// 目标执行单号。
    /// </summary>
    public string? TargetNo { get; set; }

    /// <summary>
    /// 目标执行单行 ID。
    /// </summary>
    public Guid? TargetLineId { get; set; }

    /// <summary>
    /// 已转执行数量。
    /// </summary>
    public decimal ExecutedQty { get; set; }

    /// <summary>
    /// 已入库数量。
    /// </summary>
    public decimal ReceivedQty { get; set; }

    /// <summary>
    /// 已完工数量。
    /// </summary>
    public decimal ReadyQty { get; set; }

    /// <summary>
    /// 备注。
    /// </summary>
    public string? Remark { get; set; }
}
