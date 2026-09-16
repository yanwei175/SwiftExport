using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Systems;

/// <summary>系统字典数据实体，用于保存某一字典类型下可选的业务值。</summary>
[Table("SysDictData")]
public partial class SysDictData : BaseEntity
{


    /// <summary>该实体保存的字典编码。</summary>
    public string? DictCode { get; set; }


    /// <summary>该实体保存的字典值英文名称。</summary>
    public string? DataNameEn { get; set; }


    /// <summary>该实体保存的字典值中文名称。</summary>
    public string? DataNameZh { get; set; }


    /// <summary>该实体保存的业务备注。</summary>
    public string? Remark { get; set; }


    /// <summary>该实体保存的显示排序号。</summary>
    public int? SortOrder { get; set; }


    /// <summary>该实体保存的界面样式类名。</summary>
    public string? CssClass { get; set; }


    /// <summary>系统内置标记，用于控制该记录的业务适用范围。</summary>
    public bool IsSystem { get; set; }

    /// <summary>启用标记，用于控制该记录的业务适用范围。</summary>
    public bool IsEnabled { get; set; }
}
