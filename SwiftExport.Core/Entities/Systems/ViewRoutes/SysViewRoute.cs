using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Systems.ViewRoutes;

/// <summary>
/// 视图路由配置实体
/// 职责：将 TargetViewKey 映射到对应的 ViewModel 类型名称，
/// 替代硬编码的 ViewRoutes.cs，实现路由数据库化。
/// </summary>
[Table("SysViewRoute")]
public partial class SysViewRoute : BaseEntity
{
    /// <summary>
    /// 路由键，与 SysDictType.TargetViewKey 字段值保持一致。
    /// 大小写不敏感，唯一约束由数据库保证。
    /// </summary>
    public required string TargetViewKey { get; set; }

    /// <summary>
    /// ViewModel 完整类型名（含命名空间），用于反射创建实例。
    /// 示例：SwiftExport.View.FW.ProductViews.ViewModels.ProductColorManagerViewModel
    /// </summary>
    public required string ViewModelTypeName { get; set; }

    /// <summary>
    /// View 完整类型名（可选覆盖）。
    /// 为 NULL 时，由 ViewLocatorSelector 按约定（ViewModel→View）自动解析；
    /// 不为 NULL 时，强制使用此类型，跳过约定映射（适合命名不规范的旧页面）。
    /// </summary>
    public string? ViewTypeName { get; set; }

    /// <summary>
    /// 路由配置的可读名称，仅用于管理界面展示。
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// 是否启用。false 时路由不生效（不加载该页面），但数据不删除。
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 排序号，管理列表按升序展示。
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 路由分类：Dict（字典专属页）/ Standalone（独立功能页）等。
    /// 便于后续按分类筛选路由。
    /// </summary>
    public string? RouteCategory { get; set; }

    /// <summary>
    /// 程序集名称（扩展字段）。
    /// 单程序集场景留 NULL，多程序集插件化场景填写程序集名。
    /// </summary>
    public string? AssemblyName { get; set; }

    /// <summary>
    /// 备注说明。
    /// </summary>
    public string? Remark { get; set; }
}
