namespace SwiftExport.Contracts.DTOs.Products.Requests
{
    /// <summary>按产品并发版本建立或绑定当前共享 BOM，仅授权更新 BOM 字段。</summary>
    public class ProductBomBindingRequest
    {
        public int ExpectedVersion { get; set; }
        public string BomNo { get; set; }
        /// <summary>为 true 时绑定已有有效组成，为 false 时要求编号尚无有效绑定和未删除组成。</summary>
        public bool BindExisting { get; set; }
    }
}
