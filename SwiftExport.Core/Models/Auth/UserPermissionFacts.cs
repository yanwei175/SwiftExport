namespace SwiftExport.Core.Models.Auth;

/// <summary>汇总权限投影所需的有效账号、真实角色和直接模块事实。</summary>
public sealed class UserPermissionFacts
{
    /// <summary>目标用户标识。</summary>
    public Guid UserId { get; init; }

    /// <summary>账号是否存在、活动且未删除。</summary>
    public bool IsActive { get; init; }

    /// <summary>是否通过有效 RoleId 关联拥有 Admin 角色。</summary>
    public bool IsAdmin { get; init; }

    /// <summary>有效真实角色名称，包含需要兼容的历史采购权限。</summary>
    public IReadOnlyList<string> RoleNames { get; init; } = [];

    /// <summary>独立用户模块关系中的直接分配键。</summary>
    public IReadOnlyList<string> ModulePermissions { get; init; } = [];
}
