namespace SwiftExport.Core.Entities.Auth
{
    /// <summary>角色权限关系实体，保存角色与权限之间的授权关系。</summary>
    public class SysRolePermission
    {
        /// <summary>关联的系统角色唯一标识。</summary>
        public Guid RoleId { get; set; }
        /// <summary>关联的系统权限唯一标识。</summary>
        public Guid PermissionId { get; set; }
    }
}
