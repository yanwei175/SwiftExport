using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Auth
{
    /// <summary>提交完整一级模块集合；服务端按整组替换语义处理。</summary>
    public class SaveUserModulePermissionsRequestDto
    {
        public List<string> ModulePermissions { get; set; } = new List<string>();
    }
}
