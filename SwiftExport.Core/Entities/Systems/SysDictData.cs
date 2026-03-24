using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Systems
{
    public class SysDictData : BaseEntity
    {
        public string? DictCode { get; set; }
        public string? DictValue { get; set; }
        public string? DictLabel { get; set; }
        public int SortOrder { get; set; }
        public bool IsSystem { get; set; }
        public string? CssClass { get; set; }
    }
}
