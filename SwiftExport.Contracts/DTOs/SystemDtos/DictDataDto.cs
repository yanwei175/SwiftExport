namespace SwiftExport.Contracts.DTOs.SystemDtos
{
    // 字典大类 DTO
    // 字典枚举明细 DTO
    public class DictDataDto
    {
        public int Id { get; set; }
        public string DictCode { get; set; }
        public string DictValue { get; set; }
        public string DictLabel { get; set; }
        public int SortOrder { get; set; }
        public bool IsSystem { get; set; }
        public bool IsActive { get; set; }
        public string CssClass { get; set; }
    }
}
