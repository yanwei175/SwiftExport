using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Auth;

public class UserRoleView : BaseEntity
{
    public string UserName { get; set; } = default!;
    public string? PcName { get; set; }
    public string Password { get; set; } = default!;
    public string? NickName { get; set; }
    public int RoleID { get; set; } = default!;
    public string? RoleName { get; set; }
}

