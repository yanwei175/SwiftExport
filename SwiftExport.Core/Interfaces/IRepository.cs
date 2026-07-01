using SwiftExport.Core.Common;

namespace SwiftExport.Core.Interfaces;

/// <summary>
/// 轻量泛型仓储接口。
/// 只保留所有实体都稳定适用的单实体读写能力；批量保存、动态查询和分页查询由具体业务仓储声明。
/// </summary>
public interface IRepository<T> where T : BaseEntity
{
    Task<bool> ExistsIdAsync(Guid uid);
    Task<IReadOnlyList<Guid>> GetExistingIdListAsync(IEnumerable<Guid> uidList, bool withUpdLock = false);
    Task<T?> GetByIdAsync(Guid uid, bool withUpdLock = false);
    Task<IReadOnlyList<T>> GetByIdListAsync(IEnumerable<Guid> uidList, bool withUpdLock = false);
    Task<T?> GetSingleEntityAsync();
    Task<IReadOnlyList<T>> GetAllAsync();
    Task<Guid> AddAsync(T entity);
    Task<T> UpdateAsync(T entity);
    Task<int> DeleteSoftAsync(Guid uid);
    Task<int> DeleteAsync(Guid uid, bool softDelete = true);
}
