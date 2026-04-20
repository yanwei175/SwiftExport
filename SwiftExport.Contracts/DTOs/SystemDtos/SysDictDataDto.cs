using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.SystemDtos
{
    // 字典大类 DTO
    // 字典枚举明细 DTO
    public class SysDictDataDto : BaseDto
    {
        public string DictCode { get; set; }
        public string DictValue { get; set; }
        public string DictLabel { get; set; }
        public int SortOrder { get; set; }
        public bool IsSystem { get; set; }
        public bool IsDeleted { get; set; }
        public string CssClass { get; set; }

        // 👇 新增：状态控制，废弃但不能删的数据
        public bool IsEnabled { get; set; }
        // 👇 新增：说明文字
        public string Remark { get; set; }
        // 👇 新增(可选)：如果要做省市区这种级联字典，需要这个
        public string ParentValue { get; set; }


    }
}
