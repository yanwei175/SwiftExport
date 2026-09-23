using SwiftExport.Core.Entities.Inventory;

namespace SwiftExport.Core.Interfaces.Inventory.Outbounds;

/// <summary>在调用方事务中锁定、保存和变更出库单据状态。</summary>
public interface IOutboundDocumentRepository
{
    /// <summary>锁定未删除的出库单头。</summary>
    Task<OutboundDocument?> LockHeaderAsync(Guid uid, CancellationToken token);
    /// <summary>按稳定身份顺序锁定有效明细。</summary>
    Task<IReadOnlyList<OutboundDocumentLine>> LockLinesAsync(Guid uid, CancellationToken token);
    /// <summary>检查是否存在与单据关联的正式库存事实。</summary>
    Task<bool> HasLedgerAsync(Guid uid, CancellationToken token);
    /// <summary>按版本保存已校验草稿头行。</summary>
    Task SaveAsync(OutboundDocument header, IReadOnlyList<OutboundDocumentLine> lines, int expectedVersion, CancellationToken token);
    /// <summary>在调用方事务中软删未过账草稿。</summary>
    Task DeleteAsync(OutboundDocument header, CancellationToken token);
    /// <summary>按版本更新已过账状态。</summary>
    Task MarkPostedAsync(OutboundDocument header, CancellationToken token);
    /// <summary>按版本更新整单反过账状态。</summary>
    Task MarkReversedAsync(OutboundDocument header, CancellationToken token);
}
