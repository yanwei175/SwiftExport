using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand
{
    /// <summary>携带手工来源需求身份、预期版本和关闭原因。</summary>
    public class CloseGrossDemandCommand
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public string Reason { get; set; }
    }
}
