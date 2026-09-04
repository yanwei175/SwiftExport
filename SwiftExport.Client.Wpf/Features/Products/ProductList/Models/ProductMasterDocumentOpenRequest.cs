namespace SwiftExportClient.Features.Products.ProductList.Models;

/// <summary>主型号详情文档打开参数；仅携带稳定定位值。</summary>
public sealed class ProductMasterDocumentOpenRequest
{
    /// <summary>获取或设置待打开的主型号；空值表示新建。</summary>
    public Guid ProductMasterId { get; set; }
}
