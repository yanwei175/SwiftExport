using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Auth
{
    /// <summary>提交完整可分配权限集合；服务端按整组替换语义处理并沿用既有字段名。</summary>
    public class SaveUserModulePermissionsRequestDto
    {
        public List<string> ModulePermissions { get; set; } = new List<string>();
    }
}
