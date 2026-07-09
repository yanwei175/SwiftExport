using SwiftExport.Contracts.DTOs.PackingLists;
using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Core.Interfaces;

public interface IPackingListHeadRepository : IRepository<PackingListHead>
{
    #region 订单头操作
    Task<PackingListHead?> GetByPackingListNoAsync(string packingListNo);
    #endregion

    #region 整单操作
    Task<PackingListDetailDto?> GetDetailByHeadIdAsync(Guid headId);
    #endregion
}
