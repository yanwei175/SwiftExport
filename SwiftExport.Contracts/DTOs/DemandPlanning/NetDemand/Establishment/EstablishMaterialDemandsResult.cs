namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand
{
    /// <summary>成品需求建立为物料需求的命令结果。</summary>
    public class EstablishMaterialDemandsResult
    {
        public int CreatedSourceCount { get; set; }
        public int CreatedMaterialDemandCount { get; set; }
        public int AlreadyEstablishedCount { get; set; }
    }
}
