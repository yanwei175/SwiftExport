using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.Inventory;
namespace SwiftExport.Core.Models.Procurement;
/// <summary>采购反向命令在同一事务内锁定的原始事实；用于校验及审计，不作为页面 DTO。</summary>
public sealed class ProcurementReversalFacts
{
    /// <summary>持久确认回执、审计或旧确认实体表明本单曾确认，整单草稿删除不得掩盖该历史。</summary>
    public bool HasConfirmationHistory { get; set; }
    /// <summary>可定位到本单的全部下游历史，包括软删除、反向、箱单执行及孤立流水，禁止整单删除。</summary>
    public bool HasDownstreamHistory { get; set; }
    /// <summary>存在无法归属到采购行的采购入库历史，不能证明本单没有未处理下游。</summary>
    public bool HasUnattributedInbound { get; set; }
    public PurchaseOrderHead? Head { get; set; }
    public List<PurchaseOrderLine> Lines { get; set; } = [];
    public List<GrossDemand> Sources { get; set; } = [];
    public List<NetDemand> Demands { get; set; } = [];
    public List<DemandExecutionLink> Links { get; set; } = [];
    public List<InboundDocument> Inbounds { get; set; } = [];
    public List<InboundDocumentLine> InboundLines { get; set; } = [];
    public List<InventoryLedgerEntry> Ledgers { get; set; } = [];
    public List<SupplierProgressReport> Reports { get; set; } = [];
}
