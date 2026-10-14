using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand
{
    /// <summary>从选定成品需求原子建立物料需求的幂等命令。</summary>
    public class EstablishMaterialDemandsCommand
    {
        public Guid CommandId { get; set; }
        public List<EstablishMaterialDemandItem> Items { get; set; } = new List<EstablishMaterialDemandItem>();
    }
}
