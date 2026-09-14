-- 入库非过账接口只读预检：仅查询元数据和历史风险，不执行用户表 DML/DDL。
-- 使用当前 InboundDocument/InboundDocumentLine 实体；缺字段必须停止部署，禁止猜测旧 SourceUid 身份。
SET NOCOUNT ON;

SELECT expected.TableName,expected.ColumnName,
       CASE WHEN c.column_id IS NULL THEN N'缺少字段：停止部署' ELSE N'存在：继续核对类型' END AS CheckResult,
       TYPE_NAME(c.user_type_id) AS ActualType,c.max_length,c.is_nullable
FROM (VALUES
    (N'InboundDocument',N'Uid'),(N'InboundDocument',N'Version'),(N'InboundDocument',N'DocumentNo'),
    (N'InboundDocument',N'DocumentType'),(N'InboundDocument',N'CreationMode'),(N'InboundDocument',N'LifecycleStatus'),
    (N'InboundDocument',N'PostingStatus'),(N'InboundDocument',N'WarehouseSiteId'),(N'InboundDocument',N'CommandId'),
    (N'InboundDocument',N'TriggerPackingListId'),(N'InboundDocument',N'ReceiptDate'),
    (N'InboundDocumentLine',N'Uid'),(N'InboundDocumentLine',N'InboundDocumentId'),(N'InboundDocumentLine',N'Version'),
    (N'InboundDocumentLine',N'SourceLineId'),(N'InboundDocumentLine',N'OriginalLineId'),
    (N'InboundDocumentLine',N'ProductId'),(N'InboundDocumentLine',N'Quantity'),(N'InboundDocumentLine',N'UnitSnapshot'),
    (N'PurchaseOrderHead',N'Uid'),(N'PurchaseOrderHead',N'ConfirmationStatus'),(N'PurchaseOrderHead',N'LifecycleStatus'),
    (N'PurchaseOrderLine',N'Uid'),(N'PurchaseOrderLine',N'QTY'),(N'PurchaseOrderLine',N'MaterialType'),
    (N'PurchaseOrderLine',N'UnitSnapshot'),(N'InventoryLedgerEntry',N'SourceDocumentId'),
    (N'InventoryLedgerEntry',N'SourceLineId'),(N'BusinessOperationLog',N'CommandId')
) expected(TableName,ColumnName)
LEFT JOIN sys.columns c ON c.object_id=OBJECT_ID(N'dbo.'+expected.TableName) AND c.name=expected.ColumnName
ORDER BY expected.TableName,expected.ColumnName;

-- 展示完整实体表结构及索引，由实施人核对审计、快照、默认值和旧必填字段，不仅核对新增字段。
SELECT t.name AS TableName,c.name AS ColumnName,TYPE_NAME(c.user_type_id) AS ColumnType,
       c.max_length,c.precision,c.scale,c.is_nullable,dc.definition AS DefaultDefinition
FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id
LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id
WHERE t.schema_id=SCHEMA_ID(N'dbo') AND t.name IN(N'InboundDocument',N'InboundDocumentLine')
ORDER BY t.name,c.column_id;

SELECT OBJECT_NAME(i.object_id) AS TableName,i.name,i.is_unique,i.filter_definition,
       c.name AS ColumnName,ic.key_ordinal,ic.is_included_column
FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE i.object_id IN(OBJECT_ID(N'dbo.InboundDocument'),OBJECT_ID(N'dbo.InboundDocumentLine'),OBJECT_ID(N'dbo.BusinessOperationLog'))
ORDER BY TableName,i.name,ic.key_ordinal;

IF COL_LENGTH(N'dbo.InboundDocumentLine',N'SourceLineId') IS NOT NULL
   AND COL_LENGTH(N'dbo.InboundDocument',N'CreationMode') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'
        SELECT h.DocumentType,h.CreationMode,h.PostingStatus,COUNT_BIG(*) AS LineCount
        FROM dbo.InboundDocument h JOIN dbo.InboundDocumentLine l ON l.InboundDocumentId=h.Uid
        GROUP BY h.DocumentType,h.CreationMode,h.PostingStatus;
        SELECT h.Uid AS InboundId,l.Uid AS InboundLineId,h.DocumentType,h.PostingStatus,l.SourceLineId
        FROM dbo.InboundDocument h JOIN dbo.InboundDocumentLine l ON l.InboundDocumentId=h.Uid
        LEFT JOIN dbo.PurchaseOrderLine p ON p.Uid=l.SourceLineId
        WHERE h.IsDeleted=0 AND l.IsDeleted=0 AND h.PostingStatus IN(1,8)
          AND (h.DocumentType IN(11,12) OR (h.DocumentType=10 AND (l.SourceLineId IS NULL OR p.Uid IS NULL)));';
END;
