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
    /// <summary>本单全部采购行关联的异常入库头；删行按选定行关联头判断，整单操作仍检查全部头。</summary>
    public HashSet<Guid> UnattributedInboundHeadIds { get; set; } = [];
    /// <summary>是否存在相关异常入库头，不能把整单汇总结果直接用于判断某条选定行。</summary>
    public bool HasUnattributedInbound => UnattributedInboundHeadIds.Count > 0;
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
