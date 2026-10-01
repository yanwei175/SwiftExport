// 已淘汰：旧表头别名映射链路，仅保留历史兼容；新导入使用 ExcelReadProfile／ExcelReadField 地址配置。
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
