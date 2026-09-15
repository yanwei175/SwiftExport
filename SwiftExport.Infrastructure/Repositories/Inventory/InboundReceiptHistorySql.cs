namespace SwiftExport.Infrastructure.Repositories.Inventory;

/// <summary>净收货查询共享的历史门禁：已反过账只有完整原/反向流水证明时才可按零贡献处理。</summary>
internal static class InboundReceiptHistorySql
{
    /// <summary>只读检查，不迁移历史、不把旧负数反向单或不完整反向事实当作零收货。</summary>
    internal const string Guard = """
        IF EXISTS(SELECT 1 FROM dbo.InboundDocument h
            WHERE h.IsDeleted=0 AND h.PostingStatus IN(1,8) AND h.DocumentType=10
            AND (
                NOT EXISTS(SELECT 1 FROM dbo.InboundDocumentLine l WHERE l.InboundDocumentId=h.Uid AND l.IsDeleted=0)
                OR EXISTS(SELECT 1 FROM dbo.InboundDocumentLine l WHERE l.InboundDocumentId=h.Uid AND l.IsDeleted=0
                    AND (l.Quantity<=0 OR l.OriginalLineId IS NOT NULL))
                OR (h.PostingStatus=8 AND (
                    h.CreationMode<>0 OR h.TriggerPackingListId IS NOT NULL OR h.ReversedDate IS NULL
                    OR NULLIF(LTRIM(RTRIM(h.ReverseReason)),N'') IS NULL
                    OR (SELECT COUNT(*) FROM dbo.InventoryLedgerEntry e WHERE e.SourceDocumentId=h.Uid)
                        <> 2*(SELECT COUNT(*) FROM dbo.InboundDocumentLine l WHERE l.InboundDocumentId=h.Uid AND l.IsDeleted=0)
                    OR EXISTS(SELECT 1 FROM dbo.InboundDocumentLine l WHERE l.InboundDocumentId=h.Uid AND l.IsDeleted=0
                        AND (SELECT COUNT(*) FROM dbo.InventoryLedgerEntry o
                            JOIN dbo.InventoryLedgerEntry r ON r.ReversalOfLedgerId=o.Uid
                            WHERE o.SourceDocumentId=h.Uid AND o.SourceLineId=l.Uid AND o.CommandItemId=l.Uid
                                AND o.SourceDocumentType=N'InboundDocument' AND o.TransactionType IN(10,11)
                                AND o.WarehouseSiteId=h.WarehouseSiteId AND o.ProductId=l.ProductId AND o.QuantityDelta=l.Quantity
                                AND o.UnitSnapshot=l.UnitSnapshot AND o.IsDeleted=0 AND o.ReversalOfLedgerId IS NULL
                                AND o.ReplacementOfLedgerId IS NULL AND o.CorrectionGroupId IS NULL
                                AND r.SourceDocumentId=h.Uid AND r.SourceLineId=l.Uid AND r.SourceDocumentType=N'InboundDocument'
                                AND r.TransactionType=900 AND r.QuantityDelta=-o.QuantityDelta AND r.ProductId=o.ProductId
                                AND r.WarehouseSiteId=o.WarehouseSiteId AND r.UnitSnapshot=o.UnitSnapshot AND r.IsDeleted=0
                                AND r.ReplacementOfLedgerId IS NULL AND r.CorrectionGroupId IS NULL)<>1)
                    OR EXISTS(SELECT 1 FROM dbo.InventoryLedgerEntry x JOIN dbo.InventoryLedgerEntry o ON
                        x.ReplacementOfLedgerId=o.Uid OR x.ReversalOfLedgerId=o.Uid
                        WHERE o.SourceDocumentId=h.Uid AND (x.SourceDocumentId<>h.Uid OR x.SourceDocumentId IS NULL))
                ))
            ))
            THROW 51001,N'入库反向历史不完整或存在旧负数反向单，净收货量尚未核实。',1;
        """;
}
