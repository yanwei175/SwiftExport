using System.ComponentModel.DataAnnotations.Schema;
using SwiftExport.Core.Common;
using SwiftExport.Core.DatabaseSchema;

namespace SwiftExport.Core.Entities.Shipping;

/// <summary>箱单物理行的实际执行动作及计算快照，关联生成单据和流水，不作为采购剩余库存账。</summary>
[Table("packing_list_posting_execution_line")]
[DbAppendOnly]
[DbIndex("UX_PackingListPostingExecutionLine_Number", nameof(ExecutionId), nameof(LineNumber), IsUnique = true)]
[DbIndex("IX_PackingListPostingExecutionLine_Source", nameof(GrossDemandId))]
[DbIndex("IX_PackingListPostingExecutionLine_Line", nameof(PackingListLineId))]
[DbIndex("IX_PackingListPostingExecutionLine_Ledger", nameof(LedgerId))]
[DbCheck("CK_PackingListPostingExecutionLine_Quantity", "\"qty\" > 0 AND \"line_number\" > 0")]
[DbCheck("CK_PackingListPostingExecutionLine_Action", "\"action_kind\" IN ( 'Delivery' , 'Receipt' , 'Shipment' , 'Consumption' )")]
[DbCheck("CK_PackingListPostingExecutionLine_Inventory", "( \"action_kind\" = 'Delivery' AND \"warehouse_site_id\" IS NULL AND \"document_id\" IS NULL AND \"document_line_id\" IS NULL AND \"ledger_id\" IS NULL ) OR ( \"action_kind\" <> 'Delivery' AND \"product_id\" IS NOT NULL AND \"warehouse_site_id\" IS NOT NULL AND \"document_id\" IS NOT NULL AND \"document_line_id\" IS NOT NULL AND \"ledger_id\" IS NOT NULL )")]
[LogicalReference(nameof(ExecutionId), typeof(PackingListPostingExecution), IsRequired = true)]
[LogicalReference(nameof(PackingListLineId), typeof(PackingListLine), IsRequired = true)]
public sealed class PackingListPostingExecutionLine : BaseEntity
{
    /// <summary>所属执行证据头。</summary>
    public Guid ExecutionId { get; set; }
    /// <summary>执行内稳定排序号。</summary>
    public int LineNumber { get; set; }
    /// <summary>原箱单物理行，拆箱后仍保留各行身份。</summary>
    public Guid PackingListLineId { get; set; }
    /// <summary>来源交付锚点。</summary>
    public Guid GrossDemandId { get; set; }
    /// <summary>Delivery、Receipt、Shipment、Consumption；方向由执行头表达。</summary>
    public string ActionKind { get; set; } = "";
    /// <summary>实际库存产品或无库存交付的可空产品。</summary>
    public Guid? ProductId { get; set; }
    /// <summary>实际采用的单位文本。</summary>
    public string Unit { get; set; } = "";
    /// <summary>实际库存仓，无库存交付为空。</summary>
    public Guid? WarehouseSiteId { get; set; }
    /// <summary>自动收货的采购依据，其余动作为空。</summary>
    public Guid? PurchaseLineId { get; set; }
    /// <summary>采用的根物料需求。</summary>
    public Guid? RootNetDemandId { get; set; }
    /// <summary>耗用所采用的客供件物料需求。</summary>
    public Guid? ComponentNetDemandId { get; set; }
    /// <summary>采用的根需求 GrossQty 快照。</summary>
    public decimal RootQtySnapshot { get; set; }
    /// <summary>采用的子需求 GrossQty 快照。</summary>
    public decimal ComponentQtySnapshot { get; set; }
    /// <summary>该来源本批总出货量，解释比例用量。</summary>
    public decimal ShippedQtySnapshot { get; set; }
    /// <summary>本实际动作数量，方向由执行头决定。</summary>
    public decimal Qty { get; set; }
    /// <summary>生成单据身份，无库存动作为空。</summary>
    public Guid? DocumentId { get; set; }
    /// <summary>生成单据编号快照。</summary>
    public string? DocumentNo { get; set; }
    /// <summary>生成单据当次返回的版本，用于反向前复核。</summary>
    public int? DocumentVersion { get; set; }
    /// <summary>实际入出库单据行。</summary>
    public Guid? DocumentLineId { get; set; }
    /// <summary>实际正向或反向流水。</summary>
    public Guid? LedgerId { get; set; }
    /// <summary>反向时关联原执行明细；正向为空。</summary>
    public Guid? OriginalExecutionLineId { get; set; }
}
