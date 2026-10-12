using SwiftExport.Core.Entities.Systems.SystemParameters;

namespace SwiftExport.Core.Interfaces.Systems.SystemParameters;

/// <summary>系统参数的窄持久化边界，写操作借用应用层事务。</summary>
public interface ISystemParameterRepository
{
    /// <summary>按代码注册的键读取活动参数；缺失表示尚未配置。</summary>
    Task<SystemParameter?> FindAsync(string key, CancellationToken cancellationToken = default);
    /// <summary>首次插入，键已存在时返回 false，禁止覆盖现有值。</summary>
    Task<bool> InsertAsync(SystemParameter parameter, CancellationToken cancellationToken = default);
    /// <summary>按键和预期版本更新值，成功才回填新版本。</summary>
    Task<bool> UpdateAsync(SystemParameter parameter, int expectedVersion, CancellationToken cancellationToken = default);
}
