using SwiftExport.Core.Entities.Inventory;

namespace SwiftExport.Core.Interfaces.Inventory.Inbounds;

/// <summary>入库单据锁定、草稿与状态写入持久化边界。</summary>
public interface IInboundDocumentRepository
{
    /// <summary>锁定单头，统一保护整张草稿。</summary>
    Task<InboundDocument?> LockHeaderAsync(Guid uid, CancellationToken token);
    /// <summary>读取并锁定已有有效明细。</summary>
    Task<IReadOnlyList<InboundDocumentLine>> LockLinesAsync(Guid uid, CancellationToken token);
    /// <summary>持久化已校验头行；expectedVersion 为零表示新增，否则按版本更新。</summary>
    Task SaveAsync(InboundDocument header, IReadOnlyList<InboundDocumentLine> lines, int expectedVersion, CancellationToken token);
    /// <summary>在调用方事务中软删整张单并递增版本。</summary>
    Task DeleteAsync(InboundDocument header, CancellationToken token);
    /// <summary>版本保护下更新正向过账状态，不修改原数量。</summary>
    Task MarkPostedAsync(InboundDocument header, CancellationToken token);
    /// <summary>版本保护下记录整单反过账状态及审计，不更改原始数量。</summary>
    Task MarkReversedAsync(InboundDocument header, CancellationToken token);
}
