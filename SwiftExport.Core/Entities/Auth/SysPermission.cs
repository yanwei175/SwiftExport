using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Auth
{
    // 1. 权限资源表 (对应前端的每一个菜单、按钮、敏感列)
    public class SysPermission : BaseEntity
    {
        public string Name { get; set; } = default!;       // 权限名称 (如: "删除产品")
        public string Code { get; set; } = default!;        // ★ 核心权限标识 (如: "Product:Delete")
        public string Type { get; set; } = default!;          // 类型 (Menu=菜单, Button=按钮, Column=列)
        public int ParentId { get; set; }       // 父级ID (用于构建树形菜单)
    }
}
