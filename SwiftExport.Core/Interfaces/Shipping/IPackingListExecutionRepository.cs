using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Entities.Inventory;

namespace SwiftExport.Core.Interfaces.Shipping;

/// <summary>正式执行证据及箱单状态的持久化边界，不写其他领域单据或库存。</summary>
public interface IPackingListExecutionRepository
{
    /// <summary>检查执行表及关键索引已经部署；不修改数据库。</summary>
    Task<bool> IsSchemaReadyAsync(CancellationToken token);
    /// <summary>读取命令的原始执行证据，用于相同请求重放。</summary>
    Task<PackingListPostingExecution?> FindCommandAsync(Guid commandId, CancellationToken token);
    /// <summary>读取箱单完整正反向执行证据，历史事实不按软删除过滤。</summary>
    Task<(IReadOnlyList<PackingListPostingExecution> Heads, IReadOnlyList<PackingListPostingExecutionLine> Lines)> ReadAsync(Guid headId, CancellationToken token);
    /// <summary>仅追加证据；必须由调用方事务承载，失败抛异常。</summary>
    Task AppendAsync(PackingListPostingExecution execution, IReadOnlyList<PackingListPostingExecutionLine> lines, CancellationToken token);
    /// <summary>版本保护更新过账状态，须与库存、证据和日志在一个事务中。</summary>
    Task MarkAsync(PackingListHead head, int expectedVersion, bool reverse, CancellationToken token);
    /// <summary>读取反向所需原单据、原流水和当前余额，不读取当前 BOM 或重新分配来源。</summary>
    Task<PackingListReverseFacts> ReadReverseFactsAsync(Guid headId, CancellationToken token);
}

/// <summary>整单反向的原始事实集合；异常或额外流水必须保留供上层阻断。</summary>
public sealed class PackingListReverseFacts
{
    public PackingListHead? Head { get; set; }
    public IReadOnlyList<PackingListLine> PackingLines { get; set; } = [];
    public IReadOnlyList<PackingListPostingExecution> Executions { get; set; } = [];
    public IReadOnlyList<PackingListPostingExecutionLine> Evidence { get; set; } = [];
    public IReadOnlyList<InboundDocument> InboundHeads { get; set; } = [];
    public IReadOnlyList<InboundDocumentLine> InboundLines { get; set; } = [];
    public IReadOnlyList<OutboundDocument> OutboundHeads { get; set; } = [];
    public IReadOnlyList<OutboundDocumentLine> OutboundLines { get; set; } = [];
    public IReadOnlyList<InventoryLedgerEntry> Ledgers { get; set; } = [];
    public IReadOnlyList<InventoryBalance> Balances { get; set; } = [];
    public IReadOnlyList<WarehouseSite> Warehouses { get; set; } = [];
}
