namespace SwiftExportClient.Features.QualityControl.AfterSales.Models;

/// <summary>打开售后详情文档的窄参数；空身份表示新建。</summary>
public sealed class AfterSalesDocumentOpenRequest
{
    /// <summary>需要打开的售后单身份。</summary>
    public Guid? AfterSalesId { get; init; }
}
