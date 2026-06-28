using {{ProjectName}}.Core.Common;

namespace {{ProjectName}}.Core.Interfaces;

public interface IRepository<TEntity> where TEntity : BaseEntity
{
    Task<bool> ExistsIdAsync(Guid uid);
    Task<TEntity?> GetByIdAsync(Guid uid, bool withUpdLock = false);
    Task<IReadOnlyList<TEntity>> GetByIdListAsync(IEnumerable<Guid> uidList, bool withUpdLock = false);
    Task<TEntity?> GetSingleEntityAsync();
    Task<IReadOnlyList<TEntity>> GetAllAsync();
    Task<IReadOnlyList<TEntity>> GetListAsync(string whereSql, object? param = null, bool withUpdLock = false);
    Task<PagedResult<TEntity>> GetPagedListAsync(string? whereSql = null, object? param = null, int pageIndex = 1, int pageSize = 20, string orderBy = "Uid DESC");
    Task<Guid> AddAsync(TEntity entity);
    Task<TEntity> UpdateAsync(TEntity entity);
    Task<int> DeleteSoftAsync(Guid uid);
    Task<int> DeleteAsync(Guid uid, bool softDelete = true);
}
