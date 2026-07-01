using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Infrastructure.Repositories.Orders;

public partial class SalesOrderLineRepository
{
    public Task<IReadOnlyList<Guid>> AddRangeAsync(IEnumerable<SalesOrderLine> entities) => AddRangeCoreAsync(entities);
    public Task<int> UpdateRangeAsync(IEnumerable<SalesOrderLine> entities) => UpdateRangeCoreAsync(entities);
    public Task<int> DeleteRangeAsync(IEnumerable<Guid> uidList, bool softDelete = true) => DeleteRangeCoreAsync(uidList, softDelete);
    public Task<int> DeleteRangeSoftAsync(IEnumerable<Guid> uidList) => DeleteRangeSoftCoreAsync(uidList);
}

public partial class PurchaseOrderRepository
{
    public Task<IReadOnlyList<PurchaseOrderHead>> GetListAsync(object criteriaObject, bool withUpdLock = false) => GetListCoreAsync(criteriaObject, withUpdLock);
    public Task<PagedResult<PurchaseOrderHead>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "CreatedDate DESC") => GetPagedListCoreAsync(criteria, pageIndex, pageSize, orderBy);
}

public partial class PurchaseOrderLineRepository
{
    public Task<IReadOnlyList<PurchaseOrderLine>> GetListAsync(object criteriaObject, bool withUpdLock = false) => GetListCoreAsync(criteriaObject, withUpdLock);
    public Task<IReadOnlyList<Guid>> AddRangeAsync(IEnumerable<PurchaseOrderLine> entities) => AddRangeCoreAsync(entities);
    public Task<int> UpdateRangeAsync(IEnumerable<PurchaseOrderLine> entities) => UpdateRangeCoreAsync(entities);
    public Task<int> DeleteRangeAsync(IEnumerable<Guid> uidList, bool softDelete = true) => DeleteRangeCoreAsync(uidList, softDelete);
    public Task<int> DeleteRangeSoftAsync(IEnumerable<Guid> uidList) => DeleteRangeSoftCoreAsync(uidList);
}

public partial class DependentDemandRepository
{
    public Task<IReadOnlyList<DependentDemand>> GetListAsync(QueryCriteria criteria, bool withUpdLock = false) => GetListCoreAsync(criteria, withUpdLock);
    public Task<PagedResult<DependentDemand>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "CreatedDate DESC") => GetPagedListCoreAsync(criteria, pageIndex, pageSize, orderBy);
    public Task<IReadOnlyList<Guid>> AddRangeAsync(IEnumerable<DependentDemand> entities) => AddRangeCoreAsync(entities);
    public Task<int> UpdateRangeAsync(IEnumerable<DependentDemand> entities) => UpdateRangeCoreAsync(entities);
    public Task<int> DeleteRangeAsync(IEnumerable<Guid> uidList, bool softDelete = true) => DeleteRangeCoreAsync(uidList, softDelete);
    public Task<int> DeleteRangeSoftAsync(IEnumerable<Guid> uidList) => DeleteRangeSoftCoreAsync(uidList);
}

public partial class IndependentDemandRepository
{
    public Task<PagedResult<IndependentDemand>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "CreatedDate DESC") => GetPagedListCoreAsync(criteria, pageIndex, pageSize, orderBy);
    public Task<IReadOnlyList<Guid>> AddRangeAsync(IEnumerable<IndependentDemand> entities) => AddRangeCoreAsync(entities);
    public Task<int> UpdateRangeAsync(IEnumerable<IndependentDemand> entities) => UpdateRangeCoreAsync(entities);
    public Task<int> DeleteRangeAsync(IEnumerable<Guid> uidList, bool softDelete = true) => DeleteRangeCoreAsync(uidList, softDelete);
    public Task<int> DeleteRangeSoftAsync(IEnumerable<Guid> uidList) => DeleteRangeSoftCoreAsync(uidList);
}

public partial class ReceiveLineRepository
{
    public Task<IReadOnlyList<ReceiveLine>> GetListAsync(object criteriaObject, bool withUpdLock = false) => GetListCoreAsync(criteriaObject, withUpdLock);
    public Task<IReadOnlyList<Guid>> AddRangeAsync(IEnumerable<ReceiveLine> entities) => AddRangeCoreAsync(entities);
    public Task<int> UpdateRangeAsync(IEnumerable<ReceiveLine> entities) => UpdateRangeCoreAsync(entities);
    public Task<int> DeleteRangeAsync(IEnumerable<Guid> uidList, bool softDelete = true) => DeleteRangeCoreAsync(uidList, softDelete);
    public Task<int> DeleteRangeSoftAsync(IEnumerable<Guid> uidList) => DeleteRangeSoftCoreAsync(uidList);
}

public partial class PackingListHeadRepository
{
    public Task<PagedResult<PackingListHead>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "CreatedDate DESC") => GetPagedListCoreAsync(criteria, pageIndex, pageSize, orderBy);
}

public partial class PackingListLineRepository
{
    public Task<IReadOnlyList<Guid>> AddRangeAsync(IEnumerable<PackingListLine> entities) => AddRangeCoreAsync(entities);
    public Task<int> UpdateRangeAsync(IEnumerable<PackingListLine> entities) => UpdateRangeCoreAsync(entities);
    public Task<int> DeleteRangeAsync(IEnumerable<Guid> uidList, bool softDelete = true) => DeleteRangeCoreAsync(uidList, softDelete);
    public Task<int> DeleteRangeSoftAsync(IEnumerable<Guid> uidList) => DeleteRangeSoftCoreAsync(uidList);
}

public partial class SalesOrderHeadRepository
{
    public Task<IReadOnlyList<SalesOrderHeader>> GetListAsync(object criteriaObject, bool withUpdLock = false) => GetListCoreAsync(criteriaObject, withUpdLock);
}
