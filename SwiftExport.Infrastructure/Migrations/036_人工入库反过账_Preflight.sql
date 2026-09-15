-- 人工入库整单反过账只读部署检查；不修改实体、数量约束或共享数据库数据。
-- 先完成 032 入库预检及 031 库存不可变流水预检，核实原流水唯一键和反向唯一键。
SET NOCOUNT ON;
SELECT OBJECT_NAME(c.object_id) AS TableName,c.name,TYPE_NAME(c.user_type_id) AS TypeName,c.max_length,c.precision,c.scale,c.is_nullable
FROM sys.columns c WHERE c.object_id IN(OBJECT_ID(N'dbo.InboundDocument'),OBJECT_ID(N'dbo.InboundDocumentLine'))
ORDER BY TableName,c.column_id;
SELECT name,definition,is_disabled,is_not_trusted FROM sys.check_constraints
WHERE parent_object_id IN(OBJECT_ID(N'dbo.InventoryLedgerEntry'),OBJECT_ID(N'dbo.InboundDocumentLine'));
SELECT i.name,i.is_unique,i.is_disabled,i.filter_definition,c.name AS ColumnName,ic.key_ordinal
FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE i.object_id=OBJECT_ID(N'dbo.InventoryLedgerEntry') ORDER BY i.name,ic.key_ordinal;
-- 下列查询与运行时历史门禁同口径。缺字段或历史不完整均停止部署，不自动修复。

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

SELECT N'历史反向检查通过；仍须人工核对字段、交易类型900及反向唯一索引，进行真实并发测试。' AS Result;
