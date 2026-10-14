using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;

namespace SwiftExport.Core.Interfaces.Systems.Dictionaries.Data
{
    /// <summary>
    /// 字典明细仓储接口
    /// </summary>
    public interface ISysDictDataRepository : IRepository<SysDictData>
    {
        Task<PagedResponseBase<SysDictDataDto>> GetPagedAsync(SysDictDataSearchRequest request);

        Task<IReadOnlyList<SysDictData>> GetByCodeAsync(string dictCode);

        Task<IReadOnlyList<string>> GetDictCodesWithDataAsync(IEnumerable<string> dictCodes);

        /// <summary>读取内置合并所需的数据库事实，包括软删除记录；目录负责识别和回退，锁查询沿用外层事务。</summary>
        Task<IReadOnlyList<SysDictData>> GetForBuiltInMergeAsync(string? dictCode = null, bool withUpdLock = false);

        /// <summary>在外层事务中按身份稳定锁定待维护明细，包含软删除记录以支持内置项显式恢复。</summary>
        Task<IReadOnlyList<SysDictData>> GetByIdsForUpdateAsync(IEnumerable<Guid> ids);

        /// <summary>在外层事务中保存内置明细覆盖；零版本保留固定身份首次插入，正版本只更新允许编辑的显示字段。</summary>
        Task<RepositoryWriteResult> SaveBuiltInOverrideAsync(SysDictData entity, int expectedVersion);

        /// <summary>在明细更新锁内统计采用该系列身份的有效主型号，防止删除或停用正在使用的系列。</summary>
        Task<int> CountProductSeriesReferencesAsync(Guid seriesId);

    }
}
