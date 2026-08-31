using SwiftExport.Core.Entities.Procurement;
namespace SwiftExport.Core.Interfaces.Procurement;
/// <summary>只读已冻结确认依据，不联查当前产品或当前 BOM。</summary>
public interface IProcurementConfirmationRepository
{
    /// <summary>按订单和批次返回原始行依据。</summary>
    Task<List<PurchaseConfirmationLine>> GetLinesAsync(Guid orderId, Guid confirmationId, CancellationToken ct);
    /// <summary>一次取得指定订单批次的全部节点。</summary>
    Task<List<PurchaseConfirmationNode>> GetNodesAsync(Guid orderId, Guid confirmationId, CancellationToken ct);
}
