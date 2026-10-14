namespace SwiftExportClient.Features.Products.ProductList.Models;

/// <summary>物料详情页签打开参数，只定位已有物料，不共享管理页草稿。</summary>
public sealed class ProductDocumentOpenRequest
{
    /// <summary>待打开的已保存物料身份。</summary>
    public Guid ProductId { get; set; }
}
