using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.SystemDtos
{
    // 字典大类 DTO
    // 字典枚举明细 DTO
    public class DictDataDto : BaseDto
    {
        public string DictCode { get; set; }
        public string DictValue { get; set; }
        public string DictLabel { get; set; }
        public int SortOrder { get; set; }
        public bool IsSystem { get; set; }
        public short IsDeleted { get; set; }
        public string CssClass { get; set; }
    }
}
