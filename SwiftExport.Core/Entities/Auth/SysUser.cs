using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Auth;

public class SysUser : BaseEntity
{
    public string UserName { get; set; } = default!;
    public string? PcName { get; set; }
    public string? NickName { get; set; }
    public string Password { get; set; } = default!;
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }

}

