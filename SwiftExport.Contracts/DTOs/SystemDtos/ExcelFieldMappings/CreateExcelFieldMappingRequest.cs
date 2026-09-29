namespace SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings
{
    /// <summary>创建一条由管理员维护的字段映射，不接收服务端身份或审计字段。</summary>
    public sealed class CreateExcelFieldMappingRequest
    {
        public string GroupCode { get; set; }
        public string ExcelHeader { get; set; }
        public string TargetFieldKey { get; set; }
        public bool IsEnabled { get; set; } = true;
        public string Remark { get; set; }
    }
}
