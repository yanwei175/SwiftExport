using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Auth
{
    /// <summary>返回目标用户当前直接分配的模块访问与公开业务能力集合；沿用既有 DTO 名称保持 API 兼容。</summary>
    public class UserModulePermissionsDto
    {
        public Guid UserId { get; set; }
        public bool IsAdmin { get; set; }
        public List<string> ModulePermissions { get; set; } = new List<string>();
    }
}
