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

        public bool IsEnabled { get; set; }
        // 👇 新增：说明文字
        public string? Remark { get; set; }
        // 👇 新增(可选)：如果要做省市区这种级联字典，需要这个
        public string? ParentValue { get; set; }
    }
}
