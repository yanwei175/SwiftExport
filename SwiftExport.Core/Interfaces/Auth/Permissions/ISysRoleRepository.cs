using SwiftExport.Core.Entities.Auth;

namespace SwiftExport.Core.Interfaces.Auth.Permissions;

/// <summary>
/// 角色主表仓储接口。
/// </summary>
public interface ISysRoleRepository
{
    /// <summary>获取所有未删除的角色列表。</summary>
    Task<IEnumerable<SysRole>> GetAllAsync();

    /// <summary>根据 ID 获取角色。</summary>
    Task<SysRole?> GetByIdAsync(Guid uid);

    /// <summary>根据角色名获取角色（精确匹配，忽略大小写）。</summary>
    Task<SysRole?> GetByNameAsync(string roleName);

    /// <summary>获取指定父角色下的所有子角色。</summary>
    Task<IEnumerable<SysRole>> GetChildrenAsync(Guid parentRoleId);

    /// <summary>新增角色，返回新记录 ID。</summary>
    Task<Guid> AddAsync(SysRole role);

    /// <summary>更新角色信息。</summary>
    Task<bool> UpdateAsync(SysRole role);

    /// <summary>软删除角色。</summary>
    Task<bool> DeleteAsync(Guid uid);

    /// <summary>判断角色名是否已存在（用于新增前校验）。</summary>
    Task<bool> ExistsAsync(string roleName, Guid? excludeUid = null);
}
