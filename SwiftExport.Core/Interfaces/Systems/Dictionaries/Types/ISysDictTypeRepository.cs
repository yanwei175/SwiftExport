using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.Dictionaries.Types;

namespace SwiftExport.Core.Interfaces.Systems.Dictionaries.Types;

/// <summary>提供字典类型持久事实和锁定查询；内置回退由应用层统一负责。</summary>
public interface ISysDictTypeRepository : IRepository<SysDictType>
{
    /// <summary>读取全部持久候选，包含软删除记录，以便内置类型保留已有业务身份；保存可按稳定身份锁定。</summary>
    Task<IReadOnlyList<SysDictType>> GetAllIncludingDeletedAsync(bool withUpdLock = false, CancellationToken cancellationToken = default);

    /// <summary>在外层事务内串行化显式同步，保护空表中尚不存在的内置身份；不阻塞普通字典读取。</summary>
    Task LockBuiltInSyncAsync(CancellationToken cancellationToken = default);

    /// <summary>以给定身份补齐内置类型；已有记录只恢复有效标记，保留名称、分组、层级和创建审计。</summary>
    Task<RepositoryWriteResult> SaveBuiltInAsync(SysDictType entity, int expectedVersion, CancellationToken cancellationToken = default);

    /// <summary>按数据库活动类型分页；有效内置清单的分页由字典应用服务合并后处理。</summary>
    Task<PagedResponseBase<SysDictTypeDto>> GetPagedAsync(SysDictTypeSearchRequest request);

    /// <summary>按稳定身份顺序锁定活动类型，供字典保存校验树关系。</summary>
    Task<IReadOnlyList<SysDictType>> GetAllForUpdateAsync();

    /// <summary>按编码读取活动类型，可在共享事务中锁定已有行。</summary>
    Task<IReadOnlyList<SysDictType>> GetByDictCodesAsync(IEnumerable<string> dictCodes, bool withUpdLock = false);

}
