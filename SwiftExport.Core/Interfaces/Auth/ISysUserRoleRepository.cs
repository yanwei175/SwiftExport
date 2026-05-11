using SwiftExport.Core.Entities.Auth;

namespace SwiftExport.Core.Interfaces.Auth;

/// <summary>
/// 用户-角色关联仓储接口。
/// </summary>
public interface ISysUserRoleRepository
{
    /// <summary>获取指定用户的所有角色（已启用且未删除）。</summary>
    Task<IEnumerable<SysUserRole>> GetByUserIdAsync(int userId);

    /// <summary>获取拥有指定角色的所有用户（未删除）。</summary>
    Task<IEnumerable<SysUserRole>> GetByRoleIdAsync(int roleId);

    /// <summary>判断用户是否拥有指定角色名称（忽略大小写）。</summary>
    Task<bool> UserHasRoleAsync(int userId, string roleName);

    /// <summary>为用户分配角色（插入一条关联记录）。</summary>
    Task<int> AddAsync(SysUserRole userRole);

    /// <summary>软删除用户的某条角色关联（撤销角色）。</summary>
    Task<bool> RemoveAsync(int id);

    /// <summary>删除用户的所有角色关联（重新分配前先清空）。</summary>
    Task<int> RemoveAllByUserIdAsync(int userId);
}
