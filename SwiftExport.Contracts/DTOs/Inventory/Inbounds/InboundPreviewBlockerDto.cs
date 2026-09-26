using System;
namespace SwiftExport.Contracts.DTOs.Inventory.Inbounds
{
    /// <summary>过账与反向预览共用的阻断原因，携带稳定错误码与相关行标识。</summary>
    public class InboundPreviewBlockerDto
    {
        public string Code { get; set; }
        public string Message { get; set; }
        public Guid? LineId { get; set; }
        public Guid? SourceLineId { get; set; }
    }
}
