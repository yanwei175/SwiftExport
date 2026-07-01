using SwiftExport.Core.Common;

namespace SwiftExport.Core.Interfaces;

/// <summary>
/// 复杂仓储能力接口。
/// 这些能力必须由具体业务仓储显式继承，避免所有 IRepository 默认拥有复杂保存和动态查询能力。
/// </summary>
public interface IBatchWriteRepository<T> where T : BaseEntity
{
    Task<IReadOnlyList<Guid>> AddRangeAsync(IEnumerable<T> entities);
    Task<int> UpdateRangeAsync(IEnumerable<T> entities);
    Task<int> DeleteRangeAsync(IEnumerable<Guid> uidList, bool softDelete = true);
    Task<int> DeleteRangeSoftAsync(IEnumerable<Guid> uidList);
}

public interface IWhereQueryRepository<T> where T : BaseEntity
{
    Task<IReadOnlyList<T>> GetListAsync(string whereSql, object? param = null, bool withUpdLock = false);
}

public interface IObjectQueryRepository<T> where T : BaseEntity
{
    Task<IReadOnlyList<T>> GetListAsync(object criteriaObject, bool withUpdLock = false);
}

public interface ICriteriaQueryRepository<T> where T : BaseEntity
{
    Task<IReadOnlyList<T>> GetListAsync(QueryCriteria criteria, bool withUpdLock = false);
}

public interface IPagedCriteriaRepository<T> where T : BaseEntity
{
    Task<PagedResult<T>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "CreatedDate DESC");
}
