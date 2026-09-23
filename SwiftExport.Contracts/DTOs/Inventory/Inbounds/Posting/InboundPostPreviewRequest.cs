namespace SwiftExport.Contracts.DTOs.Inventory.Inbounds.Posting
{
    /// <summary>预览已保存单据，不预占或提交库存。</summary>
    public class InboundPostPreviewRequest
    {
        public int ExpectedVersion { get; set; }
    }
}
