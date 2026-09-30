using System;

namespace SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings
{
    /// <summary>整组保存中的单行输入；已有行用版本防止覆盖其他维护者的修改。</summary>
    public sealed class SaveExcelFieldMappingRowRequest
    {
        public Guid Uid { get; set; }
        public int ExpectedVersion { get; set; }
        public string ExcelHeader { get; set; }
        public string TargetFieldKey { get; set; }
        public bool IsEnabled { get; set; }
        public string Remark { get; set; }
    }
}
