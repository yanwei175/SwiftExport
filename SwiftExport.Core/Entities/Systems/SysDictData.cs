using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Systems;

[Table("SysDictData")]
public class SysDictData : BaseEntity
{


    public string? DictCode { get; set; }


    public string? DataNameEn { get; set; }


    public string? DataNameZh { get; set; }


    public string? Remark { get; set; }


    public int? SortOrder { get; set; }


    public string? CssClass { get; set; }


    public bool IsSystem { get; set; }

    public bool IsEnabled { get; set; }
}
