using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.SystemDtos
{
    public class DictTypeDto : BaseDto
    {
        public string DictCode { get; set; }
        public string DictName { get; set; }
        public bool IsSystem { get; set; }
        public string Remark { get; set; }
    }
}
