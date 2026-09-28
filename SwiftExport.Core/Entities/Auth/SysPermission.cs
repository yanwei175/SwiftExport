using System.ComponentModel.DataAnnotations.Schema;
using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Auth;

/// <summary>代码权限目录的数据库投影；权限含义由代码目录统一定义。</summary>
[Table("SysPermissionDefinitions")]
public partial class SysPermission : BaseEntity
{
    /// <summary>不可复用的稳定权限键。</summary>
    public string PermissionKey { get; set; } = default!;

    /// <summary>管理界面显示名称。</summary>
    public string DisplayName { get; set; } = default!;

    /// <summary>权限分组键。</summary>
    public string GroupKey { get; set; } = default!;

    /// <summary>组内排序序号。</summary>
    public int SortOrder { get; set; }

    /// <summary>是否启用；停用权限不贡献有效授权。</summary>
    public bool IsActive { get; set; } = true;
}
