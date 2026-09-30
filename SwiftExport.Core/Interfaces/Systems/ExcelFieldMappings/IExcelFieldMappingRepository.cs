using SwiftExport.Core.Entities.Systems.ExcelFieldMappings;

namespace SwiftExport.Core.Interfaces.Systems.ExcelFieldMappings;

/// <summary>持久化 Excel 字段映射，复用基础仓储的单实体版本控制与审计。</summary>
public interface IExcelFieldMappingRepository : IRepository<ExcelFieldMapping>
{
    /// <summary>按组读取未删除记录；可包括停用项，供维护页面使用。</summary>
    Task<IReadOnlyList<ExcelFieldMapping>> GetByGroupAsync(string groupCode, bool includeDisabled, CancellationToken cancellationToken);

    /// <summary>在调用方事务中锁定分组记录，供整组版本校验和保存。</summary>
    Task<IReadOnlyList<ExcelFieldMapping>> GetByGroupForUpdateAsync(string groupCode, CancellationToken cancellationToken);

    /// <summary>在同一事务内暂时释放待修改记录的表头唯一键，不改变版本或审计字段。</summary>
    Task<bool> ReleaseHeadersAsync(IReadOnlyList<Guid> uids, CancellationToken cancellationToken);

    /// <summary>定位同组表头冲突；更新时由服务排除当前 Uid，数据库唯一索引仍为最终防线。</summary>
    Task<ExcelFieldMapping?> GetByNormalizedHeaderAsync(string groupCode, string normalizedHeader, CancellationToken cancellationToken);
}
