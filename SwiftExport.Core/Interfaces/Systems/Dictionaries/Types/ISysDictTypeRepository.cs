using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.Dictionaries.Types;

namespace SwiftExport.Core.Interfaces.Systems.Dictionaries.Types;

/// <summary>提供字典类型持久事实和锁定查询；内置回退由应用层统一负责。</summary>
public interface ISysDictTypeRepository : IRepository<SysDictType>
{
    /// <summary>读取全部持久候选，包含软删除记录，以便内置类型保留已有业务身份；保存可按稳定身份锁定。</summary>
    Task<IReadOnlyList<SysDictType>> GetAllIncludingDeletedAsync(bool withUpdLock = false);

    /// <summary>按数据库活动类型分页；有效内置清单的分页由字典应用服务合并后处理。</summary>
    Task<PagedResponseBase<SysDictTypeDto>> GetPagedAsync(SysDictTypeSearchRequest request);

    /// <summary>按稳定身份顺序锁定活动类型，供字典保存校验树关系。</summary>
    Task<IReadOnlyList<SysDictType>> GetAllForUpdateAsync();

    /// <summary>按编码读取活动类型，可在共享事务中锁定已有行。</summary>
    Task<IReadOnlyList<SysDictType>> GetByDictCodesAsync(IEnumerable<string> dictCodes, bool withUpdLock = false);

}
