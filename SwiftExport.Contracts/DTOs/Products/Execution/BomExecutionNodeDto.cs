using System;

namespace SwiftExport.Contracts.DTOs.Products.Execution
{
    /// <summary>一条完整 BOM 路径的组件执行快照，保留原始比例及最终四位累计比例。</summary>
    public class BomExecutionNodeDto
    {
        public string Path { get; set; }
        public Guid RootProductId { get; set; }
        public Guid ParentProductId { get; set; }
        public Guid ComponentProductId { get; set; }
        public Guid BomLineId { get; set; }
        public string ParentBomNo { get; set; }
        public int ParentBomVersion { get; set; }
        public string ParentSkuSnapshot { get; set; }
        public string ComponentSkuSnapshot { get; set; }
        public string UnitSnapshot { get; set; }
        public string MaterialType { get; set; }
        public string SupplyType { get; set; }
        public int Numerator { get; set; }
        public int Denominator { get; set; }
        public decimal CumulativeRatio { get; set; }
        public bool IsCustomerSupplied { get; set; }
    }
}
