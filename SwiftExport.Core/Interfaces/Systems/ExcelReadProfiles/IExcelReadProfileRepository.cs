using SwiftExport.Core.Entities.Systems.ExcelReadProfiles;

namespace SwiftExport.Core.Interfaces.Systems.ExcelReadProfiles;

/// <summary>地址配置聚合的持久化边界；写入必须使用应用层持有的事务并先锁定主配置。</summary>
public interface IExcelReadProfileRepository
{
    /// <summary>读取指定分组下的活动配置，按名称及主键稳定排序。</summary>
    Task<IReadOnlyList<ExcelReadProfile>> ListAsync(string groupCode, CancellationToken cancellationToken);
    /// <summary>读取主配置；写入前使用更新锁，锁保持到调用方事务结束。</summary>
    Task<ExcelReadProfile?> FindAsync(Guid uid, bool forUpdate, CancellationToken cancellationToken);
    /// <summary>读取主配置的全部活动字段。</summary>
    Task<IReadOnlyList<ExcelReadField>> GetFieldsAsync(Guid uid, CancellationToken cancellationToken);
    /// <summary>插入主配置，打审计戳并保留应用生成的主键。</summary>
    Task InsertAsync(ExcelReadProfile profile, CancellationToken cancellationToken);
    /// <summary>按预期主版本更新或软删除主配置；成功递增版本并打审计戳。</summary>
    Task<bool> UpdateAsync(ExcelReadProfile profile, int expectedVersion, CancellationToken cancellationToken);
    /// <summary>保存字段差异或软删除字段；不允许独立于主配置事务调用。</summary>
    Task SaveFieldAsync(ExcelReadField field, bool isNew, CancellationToken cancellationToken);
}

