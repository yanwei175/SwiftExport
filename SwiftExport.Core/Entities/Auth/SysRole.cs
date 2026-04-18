using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Auth;

public class SysRole : BaseEntity
{
    public string RoleName { get; set; } = default!;    // 角色名 (如: "仓库管理员")
    public string Remark { get; set; } = default!;
}

