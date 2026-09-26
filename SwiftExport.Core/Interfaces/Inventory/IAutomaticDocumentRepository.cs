using SwiftExport.Core.Entities.Inventory;

namespace SwiftExport.Core.Interfaces.Inventory;

/// <summary>自动单据专用持久化；不直接增减库存，也不打开或提交事务。</summary>
public interface IAutomaticDocumentRepository
{
    /// <summary>只读检查当前数据库自动单据所需列及明细唯一约束。</summary>
    Task<bool> IsSchemaReadyAsync(CancellationToken token);
    /// <summary>读取采购行所属供应商，不解析需求关系。</summary>
    Task<Guid?> GetPurchaseSupplierAsync(Guid purchaseLineId, CancellationToken token);
    /// <summary>在库存余额锁之前按产品身份锁定主数据。</summary>
    Task LockProductsAsync(IReadOnlyList<Guid> productIds, CancellationToken token);
    /// <summary>追加已经校验的自动入库头行，不调用手工草稿更新路径。</summary>
    Task InsertInboundAsync(InboundDocument header, IReadOnlyList<InboundDocumentLine> lines, CancellationToken token);
    /// <summary>追加已经校验的自动出库头行，保留同产品不同依据。</summary>
    Task InsertOutboundAsync(OutboundDocument header, IReadOnlyList<OutboundDocumentLine> lines, CancellationToken token);
    /// <summary>对指定箱单自动入库执行版本保护的过账或反向状态推进。</summary>
    Task SetInboundStateAsync(InboundDocument header, bool reverse, CancellationToken token);
    /// <summary>对指定箱单自动出库执行版本保护的过账或反向状态推进。</summary>
    Task SetOutboundStateAsync(OutboundDocument header, bool reverse, CancellationToken token);
}
