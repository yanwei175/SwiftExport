using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Core.Interfaces;

/// <summary>
/// 提供销售订单行读取、排序暂存及批量写入能力。
/// </summary>
public interface ISalesOrderLineRepository : IRepository<SalesOrderLine>
{
    /// <summary>读取指定销售订单的有效明细，并可按业务事务要求加更新锁。</summary>
    Task<IReadOnlyList<SalesOrderLine>> GetLinesByOrderIdAsync(Guid orderId, bool withUpdLock = false);

    /// <summary>将现有行号迁移到安全区，释放最终连续行号。</summary>
    Task<int> StageLineNumbersAsync(Guid orderId, int finalLineCount);

    /// <summary>批量新增销售订单行。</summary>
    Task<IReadOnlyList<RepositoryWriteResult>> AddLinesAsync(IEnumerable<SalesOrderLine> lines);

    /// <summary>批量更新销售订单行。</summary>
    Task<IReadOnlyList<RepositoryWriteResult>> UpdateLinesAsync(IEnumerable<SalesOrderLine> lines);

    /// <summary>按版本批量删除销售订单行。</summary>
    Task<IReadOnlyList<RepositoryWriteResult>> DeleteLinesAsync(IEnumerable<SalesOrderDeletedLineDto> lines);

    /// <summary>删除指定销售订单的全部明细。</summary>
    Task<int> DeleteByOrderIdAsync(Guid orderId);
}
