namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand
{
    /// <summary>物料需求回退命令的汇总结果。</summary>
    public class RollbackMaterialDemandsResult
    {
        public int RolledBackSourceCount { get; set; }
        public int AlreadyRolledBackCount { get; set; }
    }
}
