using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand
{
    /// <summary>按成品需求和物料修订回退活动物料需求的幂等命令。</summary>
    public class RollbackMaterialDemandsCommand
    {
        public Guid CommandId { get; set; }
        public string Reason { get; set; }
        public List<RollbackMaterialDemandItem> Items { get; set; } = new List<RollbackMaterialDemandItem>();
    }

}
