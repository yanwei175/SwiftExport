using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Core.Entities.Inventory;
namespace SwiftExport.Core.Interfaces.Procurement;
/// <summary>独立采购的事务持久化端口；不读取需求贡献或接管旧订单。</summary>
public interface IProcurementOrderRepository
{
    /// <summary>锁定命令键，读取原始回执；缺失时保护键范围。</summary>
    Task<ProcurementCommandReceipt?> GetReceiptAsync(Guid commandId, CancellationToken ct);
    /// <summary>读取新链头并锁定；旧订单返回空。</summary>
    Task<PurchaseOrderHead?> GetHeadAsync(Guid uid, CancellationToken ct);
    /// <summary>锁定全部有效行及其集合范围。</summary>
    Task<List<PurchaseOrderLine>> GetLinesAsync(Guid uid, CancellationToken ct);
    /// <summary>读取并锁定当前客户资格。</summary>
    Task<Customer?> GetCustomerAsync(Guid uid, CancellationToken ct);
    /// <summary>读取并锁定当前供应商资格。</summary>
    Task<Supplier?> GetSupplierAsync(Guid uid, CancellationToken ct);
    /// <summary>读取并锁定当前产品身份与执行属性。</summary>
    Task<Product?> GetProductAsync(Guid uid, CancellationToken ct);
    /// <summary>读取创建采购行所需的权威产品及当前客户、供应商关系快照。</summary>
    Task<ProcurementProductCandidateDto?> GetProductCandidateAsync(Guid productId, Guid customerId, Guid supplierId, CancellationToken ct);
    /// <summary>读取并锁定当前收货或消耗仓库。</summary>
    Task<WarehouseSite?> GetWarehouseAsync(Guid uid, CancellationToken ct);
    /// <summary>取得唯一启用的规范单位代码，失效或重复时返回空。</summary>
    Task<string?> GetUnitAsync(string unit, CancellationToken ct);
    /// <summary>插入新头行，依赖调用方事务。</summary>
    Task InsertAsync(PurchaseOrderHead head, IReadOnlyList<PurchaseOrderLine> lines, CancellationToken ct);
    /// <summary>仅更新商务白名单并推进头行版本。</summary>
    Task SaveAsync(PurchaseOrderHead head, IReadOnlyList<PurchaseOrderLine> lines, CancellationToken ct);
    /// <summary>固化所有行依据和节点后确认订单；任何写入失败均抛异常回滚。</summary>
    Task ConfirmAsync(PurchaseOrderHead head, IReadOnlyList<PurchaseConfirmationLine> snapshots, IReadOnlyList<PurchaseConfirmationNode> nodes, CancellationToken ct);
    /// <summary>锁定并检查采购行是否已有入库或库存下游事实。</summary>
    Task<bool> HasDownstreamFactsAsync(Guid uid, CancellationToken ct);
    /// <summary>恢复待确认并解除活动确认引用；不删除任何确认快照。</summary>
    Task CancelConfirmationAsync(PurchaseOrderHead head, CancellationToken ct);
    /// <summary>保存命令原始结果；命令键唯一。</summary>
    Task InsertReceiptAsync(ProcurementCommandReceipt receipt, CancellationToken ct);
}
