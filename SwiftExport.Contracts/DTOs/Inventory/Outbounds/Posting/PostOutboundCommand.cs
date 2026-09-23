using System;
namespace SwiftExport.Contracts.DTOs.Inventory.Outbounds
{
    /// <summary>按整单版本提交正向过账，重复请求必须复用命令标识。</summary>
    public class PostOutboundCommand
    {
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
    }
}
