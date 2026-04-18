using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Systems
{
    public class SysDictType : BaseEntity
    {
        public string? DictCode { get; set; }
        public string? DictName { get; set; }
        public bool IsSystem { get; set; }
        public string? Remark { get; set; }
    }
}
