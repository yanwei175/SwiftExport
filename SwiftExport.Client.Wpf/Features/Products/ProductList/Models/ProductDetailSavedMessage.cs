namespace SwiftExportClient.Features.Products.ProductList.Models;

/// <summary>通知管理页物料详情已提交，管理页按身份读取最新投影，不跨页共享详情草稿。</summary>
public sealed class ProductDetailSavedMessage(Guid productId)
{
    /// <summary>已保存物料的稳定身份。</summary>
    public Guid ProductId { get; } = productId;
}
