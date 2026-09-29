using SwiftExport.Core.Entities.Systems.ExcelFieldMappings;

namespace SwiftExport.Core.Interfaces.Systems.ExcelFieldMappings;

/// <summary>持久化 Excel 字段映射，复用基础仓储的单实体版本控制与审计。</summary>
public interface IExcelFieldMappingRepository : IRepository<ExcelFieldMapping>
{
    /// <summary>按组读取未删除记录；可包括停用项，供维护页面使用。</summary>
    Task<IReadOnlyList<ExcelFieldMapping>> GetByGroupAsync(string groupCode, bool includeDisabled, CancellationToken cancellationToken);

    /// <summary>定位同组表头冲突；更新时由服务排除当前 Uid，数据库唯一索引仍为最终防线。</summary>
    Task<ExcelFieldMapping?> GetByNormalizedHeaderAsync(string groupCode, string normalizedHeader, CancellationToken cancellationToken);
}
