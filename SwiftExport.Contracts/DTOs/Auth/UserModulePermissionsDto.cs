using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Auth
{
    /// <summary>返回目标用户当前直接分配的一级模块集合。</summary>
    public class UserModulePermissionsDto
    {
        public Guid UserId { get; set; }
        public bool IsAdmin { get; set; }
        public List<string> ModulePermissions { get; set; } = new List<string>();
    }
}
