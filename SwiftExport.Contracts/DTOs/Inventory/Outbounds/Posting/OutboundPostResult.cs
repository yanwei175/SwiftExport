using System;
using SwiftExport.Contracts.SysEnums.Inventory;
namespace SwiftExport.Contracts.DTOs.Inventory.Outbounds
{
    /// <summary>已提交或幂等重放的过账结果；明细通过详情接口读取。</summary>
    public class OutboundPostResult
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public string DocumentNo { get; set; }
        public OutboundPostingStatusEnum PostingStatus { get; set; }
    }
}
