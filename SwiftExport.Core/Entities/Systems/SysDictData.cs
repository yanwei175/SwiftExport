using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Systems
{

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
}
