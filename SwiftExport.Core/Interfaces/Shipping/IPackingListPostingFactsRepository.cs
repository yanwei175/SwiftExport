using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.Shipping;

namespace SwiftExport.Core.Interfaces.Shipping;

/// <summary>过账专用事实读取边界；只读取真实领域事实，不决定分配方案、不写库存。</summary>
public interface IPackingListPostingFactsRepository
{
    /// <summary>读取箱单和计算所需事实；不加写锁、不预留额度。</summary>
    Task<PackingListPostingFacts> ReadAsync(Guid packingListId, CancellationToken token);
    /// <summary>在调用方事务中按来源、物料、Link 顺序锁定相关范围；必须先于采购及库存资源准备。</summary>
    Task LockSourcesAsync(IReadOnlyList<Guid> grossDemandIds, CancellationToken token);
}

/// <summary>过账领域内部事实集合，不直接作为 API 响应；交付净额只来自有效已过账箱单。</summary>
public sealed class PackingListPostingFacts
{
    public PackingListHead? Head { get; set; }
    public IReadOnlyList<PackingListLine> Lines { get; set; } = [];
    public IReadOnlyList<GrossDemand> Sources { get; set; } = [];
    public IReadOnlyList<NetDemand> Demands { get; set; } = [];
    public IReadOnlyList<DemandExecutionLink> Links { get; set; } = [];
    public IReadOnlyList<PurchaseOrderHead> PurchaseHeads { get; set; } = [];
    public IReadOnlyList<PurchaseOrderLine> PurchaseLines { get; set; } = [];
    public IReadOnlyList<Product> Products { get; set; } = [];
    public IReadOnlyList<WarehouseSite> Warehouses { get; set; } = [];
    public IReadOnlyList<InventoryBalance> Balances { get; set; } = [];
    public IReadOnlyDictionary<Guid, decimal> Delivered { get; set; } = new Dictionary<Guid, decimal>();
    public bool CustomerExists { get; set; }
}
