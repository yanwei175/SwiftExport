using SwiftExport.Core.Entities.Procurement;
namespace SwiftExport.Core.Interfaces.Procurement;
/// <summary>独立采购订单头行锁定及创建、商务保存、确认的事务持久化端口。</summary>
public interface IProcurementOrderDocumentRepository
{
    /// <summary>读取新链头并锁定；旧订单返回空。</summary>
    Task<PurchaseOrderHead?> GetHeadAsync(Guid uid, CancellationToken ct);
    /// <summary>锁定全部有效行及其集合范围。</summary>
    Task<List<PurchaseOrderLine>> GetLinesAsync(Guid uid, CancellationToken ct);
    /// <summary>插入新头行，依赖调用方事务。</summary>
    Task InsertAsync(PurchaseOrderHead head, IReadOnlyList<PurchaseOrderLine> lines, CancellationToken ct);
    /// <summary>仅更新商务白名单并推进头行版本。</summary>
    Task SaveAsync(PurchaseOrderHead head, IReadOnlyList<PurchaseOrderLine> lines, CancellationToken ct);
    /// <summary>保存采购确认状态、批次与审计；不生成 BOM 节点或库存事实。</summary>
    Task ConfirmAsync(PurchaseOrderHead head, CancellationToken ct);
}
