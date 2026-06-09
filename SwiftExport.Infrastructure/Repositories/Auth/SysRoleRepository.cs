using Dapper;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Infrastructure.Repositories.Auth;

/// <summary>
/// 角色主表数据访问实现，对应 [dbo].[SysRoles]。
/// </summary>
public class SysRoleRepository(DbSession session) : ISysRoleRepository
{
    private readonly DbSession _session = session;

    private const string SelectColumns = """
        Uid, RoleName, Description, ParentRoleId, ChangeTime,
        IsDeleted, CreatedDate, CreatedBy, UpdatedDate, UpdatedBy
        """;

    /// <summary>获取所有未删除的角色</summary>
    public async Task<IEnumerable<SysRole>> GetAllAsync()
    {
        const string sql = $"""
            SELECT {SelectColumns}
            FROM [SysRoles] WITH(NOLOCK)
            WHERE (IsDeleted = 0 OR IsDeleted IS NULL)
            ORDER BY RoleName
            """;
        return await _session.Connection.QueryAsync<SysRole>(sql);
    }

    /// <summary>根据 ID 查询角色</summary>
    public async Task<SysRole?> GetByIdAsync(Guid uid)
    {
        const string sql = $"""
            SELECT {SelectColumns}
            FROM [SysRoles] WITH(NOLOCK)
            WHERE Uid = @Uid AND (IsDeleted = 0 OR IsDeleted IS NULL)
            """;
        return await _session.Connection.QuerySingleOrDefaultAsync<SysRole>(sql, new { Uid = uid });
    }

    /// <summary>根据角色名查询（精确匹配）</summary>
    public async Task<SysRole?> GetByNameAsync(string roleName)
    {
        const string sql = $"""
            SELECT {SelectColumns}
            FROM [SysRoles] WITH(NOLOCK)
            WHERE RoleName = @RoleName AND (IsDeleted = 0 OR IsDeleted IS NULL)
            """;
        return await _session.Connection.QuerySingleOrDefaultAsync<SysRole>(
            sql, new { RoleName = roleName });
    }

    /// <summary>获取指定父角色的所有子角色</summary>
    public async Task<IEnumerable<SysRole>> GetChildrenAsync(Guid parentRoleId)
    {
        const string sql = $"""
            SELECT {SelectColumns}
            FROM [SysRoles] WITH(NOLOCK)
            WHERE ParentRoleId = @ParentRoleId AND (IsDeleted = 0 OR IsDeleted IS NULL)
            ORDER BY RoleName
            """;
        return await _session.Connection.QueryAsync<SysRole>(sql, new { ParentRoleId = parentRoleId });
    }

    /// <summary>新增角色，返回新 Id</summary>
    public async Task<Guid> AddAsync(SysRole role)
    {
        const string sql = """
            INSERT INTO [SysRoles]
                (RoleName, Description, ParentRoleId, ChangeTime,
                 IsDeleted, CreatedDate, CreatedBy, UpdatedDate, UpdatedBy)
            OUTPUT INSERTED.Uid
            VALUES
                (@RoleName, @Description, @ParentRoleId, @ChangeTime,
                 0, @CreatedDate, @CreatedBy, @UpdatedDate, @UpdatedBy)
            """;
        return await _session.Connection.ExecuteScalarAsync<Guid>(sql, role);
    }

    /// <summary>更新角色信息</summary>
    public async Task<bool> UpdateAsync(SysRole role)
    {
        const string sql = """
            UPDATE [SysRoles]
            SET RoleName     = @RoleName,
                Description  = @Description,
                ParentRoleId = @ParentRoleId,
                ChangeTime   = @ChangeTime,
                UpdatedDate  = @UpdatedDate,
                UpdatedBy    = @UpdatedBy
            WHERE Uid = @Uid AND (IsDeleted = 0 OR IsDeleted IS NULL)
            """;
        var rows = await _session.Connection.ExecuteAsync(sql, role);
        return rows > 0;
    }

    /// <summary>软删除角色</summary>
    public async Task<bool> DeleteAsync(Guid uid)
    {
        const string sql = """
            UPDATE [SysRoles]
            SET IsDeleted = 1, UpdatedDate = GETDATE()
            WHERE Uid = @Uid
            """;
        var rows = await _session.Connection.ExecuteAsync(sql, new { Uid = uid });
        return rows > 0;
    }

    /// <summary>判断角色名是否已存在（新增或重命名时用于唯一性校验）</summary>
    public async Task<bool> ExistsAsync(string roleName, Guid? excludeUid = null)
    {
        const string sql = """
            SELECT COUNT(1) FROM [SysRoles] WITH(NOLOCK)
            WHERE RoleName = @RoleName
              AND (IsDeleted = 0 OR IsDeleted IS NULL)
              AND (@ExcludeUid IS NULL OR Uid <> @ExcludeUid)
            """;
        var count = await _session.Connection.ExecuteScalarAsync<int>(
            sql, new { RoleName = roleName, ExcludeUid = excludeUid });
        return count > 0;
    }
}
