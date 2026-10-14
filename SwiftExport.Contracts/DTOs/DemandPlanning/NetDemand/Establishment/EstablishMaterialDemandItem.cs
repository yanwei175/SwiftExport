using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand
{
    /// <summary>指定一条成品需求及客户端预览时采用的版本。</summary>
    public class EstablishMaterialDemandItem
    {
        public Guid GrossDemandId { get; set; }
        public int SourceDemandVersion { get; set; }
        public int? SourceVersion { get; set; }
    }
}
