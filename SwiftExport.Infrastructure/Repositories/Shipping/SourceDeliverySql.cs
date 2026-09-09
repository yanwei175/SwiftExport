namespace SwiftExport.Infrastructure.Repositories.Shipping;

/// <summary>集中定义正式客户交付净额口径，供交付查询和 PL 投影复用，避免产生第二套可发量公式。</summary>
internal static class SourceDeliverySql
{
    /// <summary>按 GrossDemandId 汇总已过账正式客户出库；反向后的非已过账单据自然退出净额。</summary>
    internal const string FinalOutboundApply = """
        OUTER APPLY(
            SELECT SUM(line.Quantity) AS DeliveredQty
            FROM dbo.OutboundDocumentLine line
            INNER JOIN dbo.OutboundDocument document ON document.Uid=line.OutboundDocumentId
            WHERE line.GrossDemandId=g.Uid
              AND document.DocumentType=20 AND document.PostingStatus=1
              AND ISNULL(line.IsDeleted,0)=0 AND ISNULL(document.IsDeleted,0)=0
        ) delivery
        """;
}
