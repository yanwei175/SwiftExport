namespace SwiftExport.Core.Models.Auth;

/// <summary>汇总当前账号、有效角色编码及角色权限事实，供统一权限投影计算。</summary>
public sealed class UserPermissionFacts
{
    /// <summary>目标用户标识。</summary>
    public Guid UserId { get; init; }

    /// <summary>账号是否存在、活动且未删除。</summary>
    public bool IsActive { get; init; }

    /// <summary>是否通过有效 RoleId 关联拥有内置 SYSTEM_ADMIN 角色。</summary>
    public bool IsAdmin { get; init; }

    /// <summary>有效角色的当前显示名称。</summary>
    public IReadOnlyList<string> RoleNames { get; init; } = [];

    /// <summary>有效角色的稳定编码。</summary>
    public IReadOnlyList<string> RoleCodes { get; init; } = [];

    /// <summary>有效角色拥有的活动权限键并集。</summary>
    public IReadOnlyList<string> PermissionKeys { get; init; } = [];

}
