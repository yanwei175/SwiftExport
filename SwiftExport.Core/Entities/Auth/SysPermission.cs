using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Auth
{
    // 1. 权限资源表 (对应前端的每一个菜单、按钮、敏感列)
    /// <summary>系统权限实体，用于定义菜单、按钮或字段级权限及其层级。</summary>
    public class SysPermission : BaseEntity
    {
        /// <summary>权限名称 (如: &quot;删除产品&quot;)。</summary>
        public string Name { get; set; } = default!;       // 权限名称 (如: "删除产品")
        /// <summary>★ 核心权限标识 (如: &quot;Product:Delete&quot;)。</summary>
        public string Code { get; set; } = default!;        // ★ 核心权限标识 (如: "Product:Delete")
        /// <summary>类型 (Menu=菜单, Button=按钮, Column=列)。</summary>
        public string Type { get; set; } = default!;          // 类型 (Menu=菜单, Button=按钮, Column=列)
        /// <summary>父级ID (用于构建树形菜单)。</summary>
        public int ParentId { get; set; }       // 父级ID (用于构建树形菜单)
    }
}
