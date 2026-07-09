using SwiftExport.Contracts.Wrappers;
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
    Task<RepositoryWriteResult> AddAsync(T entity);
    Task<RepositoryWriteResult> UpdateAsync(T entity);
    Task<RepositoryWriteResult> DeleteAsync(Guid uid, int version, bool softDelete = true);
}
