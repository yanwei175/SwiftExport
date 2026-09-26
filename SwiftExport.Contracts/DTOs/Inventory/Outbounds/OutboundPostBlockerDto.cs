using System;
namespace SwiftExport.Contracts.DTOs.Inventory.Outbounds
{
    /// <summary>稳定错误代码及对应行定位，不依赖消息文本分类。</summary>
    public class OutboundPostBlockerDto
    {
        public string Code { get; set; }
        public string Message { get; set; }
        public Guid? LineId { get; set; }
    }
}
