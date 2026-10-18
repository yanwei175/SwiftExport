namespace SwiftExport.Core.Interfaces.Shipping;

/// <summary>装箱单来源候选的唯一默认包装与客户参考号补充信息。</summary>
public sealed class PackingListSourceEnrichment
{
    /// <summary>成品需求标识。</summary>
    public Guid GrossDemandId { get; set; }
    /// <summary>唯一默认包装的每包数量。</summary>
    public decimal? PcsPerPackage { get; set; }
    /// <summary>唯一默认包装的单包净重。</summary>
    public decimal? NwPerPackage { get; set; }
    /// <summary>唯一默认包装的单包毛重。</summary>
    public decimal? GwPerPackage { get; set; }
    /// <summary>唯一默认包装的单包体积。</summary>
    public decimal? CbmPerPackage { get; set; }
    /// <summary>经客户与头行归属核验的销售或售后参考号。</summary>
    public string? CustomerPoNo { get; set; }
        /// <summary>销售发票号或系统售后单号，仅用于来源查询和展示。</summary>
        public string? InvoiceNo { get; set; }
}
