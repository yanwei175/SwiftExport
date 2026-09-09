using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand
{
    /// <summary>一条待回退的来源物料需求修订。</summary>
    public class RollbackMaterialDemandItem
    {
        public Guid GrossDemandId { get; set; }
        public int DecompositionRevision { get; set; }
    }
}
