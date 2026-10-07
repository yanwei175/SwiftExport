using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Systems.Dictionaries.Types;

/// <summary>系统字典类型实体，保存分类、层级、分组和排序，不配置客户端页面。</summary>
[Table("sys_dict_type")]
public partial class SysDictType : BaseEntity
{
    /// <summary>该实体保存的字典编码。</summary>
    public required string DictCode { get; set; }
    /// <summary>该实体保存的字典名称。</summary>
    public required string DictName { get; set; }
    /// <summary>系统内置标记，用于控制该记录的业务适用范围。</summary>
    public bool IsSystem { get; set; }
    /// <summary>该实体保存的业务备注。</summary>
    public string? Remark { get; set; }
    // 新增：支撑树形结构
    /// <summary>关联的上级记录唯一标识。</summary>
    public Guid? ParentId { get; set; }
    // 新增：支撑左侧树的排序
    /// <summary>该实体保存的显示排序号。</summary>
    public int? SortOrder { get; set; }

    // 👇 新增：组别标识（用于逻辑分组）
    /// <summary>如 &quot;Basic&quot; (基础档案), &quot;Finance&quot; (财务), &quot;Sales&quot; (销售)。</summary>
    public string? GroupCode { get; set; } // 如 "Basic" (基础档案), "Finance" (财务), "Sales" (销售)
    /// <summary>组名，如 &quot;基础数据&quot;。</summary>
    public string? GroupName { get; set; } // 组名，如 "基础数据"
}
