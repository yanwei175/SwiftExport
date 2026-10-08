namespace SwiftExport.Contracts.DTOs.Products
{
    /// <summary>编号的当前共享事实摘要；预览不预占编号，正式绑定仍须在事务内复核。</summary>
    public class BomNumberInfoDto
    {
        public string BomNo { get; set; }
        public int BoundProductCount { get; set; }
        public int ComponentCount { get; set; }
        public int StoredComponentCount { get; set; }
        public bool HasInvalidComponents { get; set; }
    }
}
