namespace SwiftExport.Contracts.DTOs.Inventory.Outbounds
{
    /// <summary>预览已保存单据，不预占或提交库存。</summary>
    public class OutboundPostPreviewRequest
    {
        public int ExpectedVersion { get; set; }
    }
}
